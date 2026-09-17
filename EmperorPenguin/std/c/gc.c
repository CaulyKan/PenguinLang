/* EmperorPenguin garbage collector (GC v3, green tea).
 *
 * Heap layout: small objects (24B GCHeader + body, slot <= 512B) live in
 * 8KiB size-class spans cut from aligned superchunks (gc_span.c); larger
 * objects are malloc blocks on _emperor_gc_allocation_list with the
 * incrementally maintained sorted start-address index (interior-pointer
 * resolution via binary search).
 *
 * Roots: per-function frame descriptors (emitted by LLVMEmitter; bare-ref
 * and ref-map struct slots), global roots (incl. value-class/enum globals
 * as conservative scan regions), typed container-buffer regions
 * (#__track_buffer), meta-pinned objects, the fresh-object quarantine
 * ring, and an always-on conservative main-stack cover (safe in a
 * non-moving collector: a hit is just an extra mark).
 *
 * Collection: single-generation, non-moving, stop-the-world.
 * _emperor_gc_poll (emitted before every call/alloc) drains the heap-goal
 * flag with gc_collect_greentea: precise roots -> gt_drain (span-batched
 * gray/black bitmap marking interleaved with the large-object worklist) ->
 * GT-VERIFY invariants -> sweep (malloc pass1 unlink+finalize, span
 * finalizer pass, malloc pass2, span reclaim) -> unified goal update (ONE
 * budget over the combined malloc+span heap — see gc_update_unified_goal).
 * "conservative" (EMPEROR_GC_MODE) keeps the pre-v2 inline-collect
 * fallback. The v2 generational machine (nursery, minors, promotion/
 * pinning, card table, write barriers) was deleted at M4b; the barrier
 * symbols remain as no-ops for previously emitted .ll until the M4c
 * emitter change.
 *
 * Env knobs (see also emperor_gc.h): GC_VERIFY, EMPEROR_GC_YOUNG (the
 * unified goal's additive churn slack), EMPEROR_GC_GOAL_FACTOR /
 * EMPEROR_GC_MIN_HEAP, EMPEROR_GC_STATS,
 * GC_PROFILE, EMPEROR_GC_STRESS_EVERY/_MAX, EMPEROR_GC_DISABLE.
 *
 * File map: index/resolve (sorted index, gc_resolve_any) -> regions ->
 * frame roots -> quarantine -> stats -> mark/drain (gc_scan_object_body) ->
 * ref-map walk -> sweep (pass1/pass2) -> greentea driver -> conservative
 * collect (gc_collect_main) -> init -> barriers (no-ops) -> allocation ->
 * poll. The span heap itself lives in gc_span.c (shared interface:
 * include/gc_internal.h). */
#include "emperor_types.h"
#include "emperor_gc.h"
#include "gc_internal.h"
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdio.h>
#include <time.h>
#include <setjmp.h>
#if defined(_WIN32)
#  include <windows.h>
#endif

/* Conservative stack/region scanning reads every word in range, including
 * sanitizer stack redzones — exempt it from ASan so instrumented builds of
 * the runtime don't abort on a benign read. */
#if defined(__has_feature)
#  if __has_feature(address_sanitizer)
#    define EMPEROR_NO_ASAN __attribute__((no_sanitize("address")))
#  endif
#endif
#ifndef EMPEROR_NO_ASAN
#  define EMPEROR_NO_ASAN
#endif

/* ---- GC Header (shared with gc_span.c via gc_internal.h) ---- */

static GCHeader* _emperor_gc_allocation_list = NULL;
static size_t _emperor_gc_total_allocated = 0;
static size_t _emperor_gc_threshold = 256 * 1024; /* 256KB initial */

/* ---- Unified dual-heap goal (GC v3.1, spec §9.2) ----
 * ONE collection budget for the malloc side AND the span heap (the split
 * goals it replaces are documented in gc_internal.h). Greentea only —
 * conservative mode has no span heap and keeps _emperor_gc_threshold. */
static size_t _gc_unified_goal = GC_GOAL_MIN_HEAP; /* floors at min_heap */
static size_t _gc_goal_factor = GC_GOAL_FACTOR;    /* EMPEROR_GC_GOAL_FACTOR */
static size_t _gc_goal_min_heap = GC_GOAL_MIN_HEAP;/* EMPEROR_GC_MIN_HEAP */

/* Runtime kill-switch: set EMPEROR_GC_DISABLE=1 in the environment to raise
 * the threshold to 8TB so automatic collection never fires (diagnostics —
 * tells GC-induced crashes apart from logic bugs without recompiling).
 * Explicit gc_collect() calls are never gated. gc_internal.h: gc_span.c's
 * allocation trigger reads it. */
int _emperor_gc_disabled = 0;

/* (The former tracked-pointer hash set was removed: marking switched to
 * interior-aware block resolution via gc_resolve_block, and nothing read the
 * set anymore — yet every allocation inserted into it and every sweep
 * rebuilt it whole. Both were pure overhead.) */

/* ---- Interior-pointer resolution ----
 * Inline value-type layouts (value-class fields nested inside heap objects,
 * enum payloads, ref<ValueClass> bindings) produce pointers INTO the middle
 * of a GC allocation, not to its base. Exact-pointer matching then misses the
 * owner, the object is swept while still referenced through that interior
 * pointer, and the program corrupts the heap (tcache reuse overwrites the
 * still-used object). The sorted-starts index resolves any candidate address
 * to its owning block (greatest start <= candidate, candidate < start+size)
 * with one binary search, so marking treats interior pointers as roots of
 * their owner — conservative (may retain a dead block a stray integer points
 * into) but never frees a live one.
 *
 * The index is maintained INCREMENTALLY: allocations append to the pending
 * set below, each collection sorts just that (small) set and merges it into
 * the persistent sorted array (linear back-merge), and the sweep removes the
 * dead entries in the same pass. The previous full-list qsort per collection
 * was O(n log n) over the WHOLE heap and dominated GC time on
 * multi-million-block heaps (LSP recompile profile: ~30% of all cycles).
 * Allocation between refreshes is fine because a fresh block cannot be
 * pointed to by a stale interior pointer (it did not exist when the pointer
 * was created). */
static void** _emperor_gc_sorted = NULL;
static size_t _emperor_gc_sorted_count = 0;
static size_t _emperor_gc_sorted_capacity = 0;

/* Blocks allocated since the last index refresh (unsorted user pointers).
 * _gc_sorted_stale marks a pending-append failure: the next refresh falls
 * back to a full rebuild so no live block can ever be missing from the
 * index (a missing block would let marking free it prematurely). */
static void** _emperor_gc_pending = NULL;
static size_t _emperor_gc_pending_count = 0;
static size_t _emperor_gc_pending_capacity = 0;
static int _emperor_gc_sorted_stale = 0;


static int gc_ptr_cmp(const void* a, const void* b) {
    void* pa = *(void* const*)a;
    void* pb = *(void* const*)b;
    return pa < pb ? -1 : (pa > pb ? 1 : 0);
}




static int gc_rebuild_sorted(void) {
    size_t n = 0;
    for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) n++;
    if (n > _emperor_gc_sorted_capacity) {
        size_t new_capacity = n * 2;
        void** grown = (void**)realloc(_emperor_gc_sorted, new_capacity * sizeof(void*));
        if (!grown) { _emperor_gc_sorted_count = 0; return 0; }
        _emperor_gc_sorted = grown;
        _emperor_gc_sorted_capacity = new_capacity;
    }
    size_t i = 0;
    for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next)
        _emperor_gc_sorted[i++] = (char*)h + sizeof(GCHeader);
    qsort(_emperor_gc_sorted, n, sizeof(void*), gc_ptr_cmp);
    _emperor_gc_sorted_count = n;
    return 1;
}

/* Bring the index up to date at collection start: sort ONLY the blocks
 * allocated since the last refresh and merge them into the persistent
 * sorted array. Cost is O(p log p + n) with p = new blocks, instead of the
 * full rebuild's O(n log n). Falls back to a full rebuild when the index
 * was marked stale (pending-append failure) or on merge-buffer growth
 * failure; a full-rebuild failure retains everything this cycle (caller). */
static int gc_refresh_sorted(void) {
    if (_emperor_gc_sorted_stale || _emperor_gc_pending_count == 0) {
        int was_stale = _emperor_gc_sorted_stale;
        _emperor_gc_pending_count = 0;
        _emperor_gc_sorted_stale = 0;
        return was_stale ? gc_rebuild_sorted() : 1;
    }
    size_t p = _emperor_gc_pending_count;
    size_t n = _emperor_gc_sorted_count + p;
    if (n > _emperor_gc_sorted_capacity) {
        size_t new_capacity = n * 2;
        void** grown = (void**)realloc(_emperor_gc_sorted, new_capacity * sizeof(void*));
        if (!grown) {
            _emperor_gc_pending_count = 0;
            _emperor_gc_sorted_stale = 0;
            return gc_rebuild_sorted();
        }
        _emperor_gc_sorted = grown;
        _emperor_gc_sorted_capacity = new_capacity;
    }
    qsort(_emperor_gc_pending, p, sizeof(void*), gc_ptr_cmp);
    /* Back-merge into sorted[0..n): the write index is always >= both read
     * indices, so the destination never overwrites an unread source slot. */
    size_t i = _emperor_gc_sorted_count;
    size_t j = p;
    size_t w = n;
    while (j > 0) {
        if (i > 0 && (char*)_emperor_gc_sorted[i - 1] > (char*)_emperor_gc_pending[j - 1]) {
            _emperor_gc_sorted[--w] = _emperor_gc_sorted[--i];
        } else {
            _emperor_gc_sorted[--w] = _emperor_gc_pending[--j];
        }
    }
    /* sorted[0..i) are already in their final places. */
    _emperor_gc_sorted_count = n;
    _emperor_gc_pending_count = 0;
    return 1;
}

/* Resolve a candidate address to its owning block's user pointer, or NULL.
 * Exact bases resolve too (candidate == start). */
static void* gc_resolve_block(void* candidate) {
    if (_emperor_gc_sorted_count == 0) return NULL;
    char* c = (char*)candidate;
    size_t lo = 0, hi = _emperor_gc_sorted_count;
    /* find greatest index with sorted[idx] <= candidate */
    while (lo + 1 < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if ((char*)_emperor_gc_sorted[mid] <= c) lo = mid;
        else hi = mid;
    }
    if ((char*)_emperor_gc_sorted[lo] > c) return NULL;
    GCHeader* h = (GCHeader*)((char*)_emperor_gc_sorted[lo] - sizeof(GCHeader));
    if (c < (char*)_emperor_gc_sorted[lo] + h->size) return _emperor_gc_sorted[lo];
    return NULL;
}

/* ---- Platform-specific stack pointer ---- */

#if defined(__x86_64__)
static inline void* _emperor_gc_get_stack_pointer(void) {
    void* sp;
    __asm__ volatile ("mov %%rsp, %0" : "=r"(sp));
    return sp;
}
#elif defined(__aarch64__)
static inline void* _emperor_gc_get_stack_pointer(void) {
    void* sp;
    __asm__ volatile ("mov %0, sp" : "=r"(sp));
    return sp;
}
#else
static inline void* _emperor_gc_get_stack_pointer(void) {
    return __builtin_frame_address(0);
}
#endif

static void* _emperor_gc_stack_bottom = NULL;

/* ---- GC Roots ---- */

#define EMPEROR_GC_MAX_ROOTS 65536
static void** _emperor_gc_global_roots[EMPEROR_GC_MAX_ROOTS];
static int _emperor_gc_global_root_count = 0;

void _emperor_gc_add_root(void** root) {
    if (_emperor_gc_global_root_count < EMPEROR_GC_MAX_ROOTS) {
        _emperor_gc_global_roots[_emperor_gc_global_root_count++] = root;
    } else {
        /* Dropping a root silently would let the collector free a live
         * global; make the overflow loud instead. */
        static int warned = 0;
        if (!warned) {
            fprintf(stderr, "emperor gc: root registry overflow (%d), further globals are NOT rooted\n", EMPEROR_GC_MAX_ROOTS);
            warned = 1;
        }
    }
}

/* Unregister a root slot (swap-remove; order is irrelevant — marking walks
 * the whole array). Unregistering an unknown slot is a no-op. Used by the
 * scheduler to drop the async ctx root once the entry consumes it. */
void _emperor_gc_remove_root(void** root) {
    for (int i = 0; i < _emperor_gc_global_root_count; i++) {
        if (_emperor_gc_global_roots[i] == root) {
            _emperor_gc_global_roots[i] = _emperor_gc_global_roots[--_emperor_gc_global_root_count];
            return;
        }
    }
}

/* ---- External scan regions ----
 * Raw (non-GC) buffers that CONTAIN pointers to GC objects: std container
 * element storage (Vector<T>/HashMap<K,V> buffers, Array<T,N> with reference
 * elements). The conservative stack/object scan cannot see inside malloc'd
 * memory, so containers register their buffers here and marking treats each
 * region as an extension of the stack. Containers unregister in dispose_mem
 * — which the GC finalizer runs on dead instances — so the finalizer is
 * load-bearing for correctness: a region that outlives its buffer only
 * retains garbage (safe), while a buffer missing its region would have live
 * elements swept (unsafe). */

typedef struct {
    char* base;
    size_t bytes;
    /* Low bound of the LIVE portion of this region ([live_lo, base+bytes) is
     * scanned). For raw buffer registrations (container storage, value-class
     * globals) it stays == base (everything is live). The coroutine scheduler
     * narrows it to each coroutine's parked stack pointer via
     * _emperor_gc_scan_set_live, so dead frames below a parked/finished sp —
     * pure stale garbage that a conservative scan would pin forever — are
     * excluded (matches the main stack's watermark treatment). The RUNNING
     * coroutine's region is additionally capped at the collect-time sp in
     * _emperor_gc_collect. */
    char* live_lo;
    /* Typed registration (GC v2 phase 3b): ELEM_COUNT elements of
     * ELEM_STRIDE bytes each, whose reference layout one element is
     * described by ELEM_MAP (an emperor ref-map program; bare-reference
     * elements use _emperor_gc_bare_refmap). ELEM_MAP == NULL keeps the
     * legacy conservative whole-byte scan. A typed region is walked
     * precisely: major marks element slots, a minor REWRITES young
     * references in place (a conservative region can only pin them). */
    uint64_t elem_count;
    uint64_t elem_stride;
    const int32_t* elem_map;
} GCScanRegion;

/* Shared ref-map for buffers of bare references (one pointer per element). */
const int32_t _emperor_gc_bare_refmap[5] = {5, 1, 1, 0, -1};

/* Pin-marker slot "map": a frame slot whose value must be PINNED at its
 * address, never promoted. Such slots mirror a reference whose live copies
 * ALSO exist as raw SSA values / clang spill words the collector cannot see
 * or rewrite — scalar ref/string parameters and reference `this` receivers
 * (the body's uses keep the SSA parameter). Promoting the target would
 * leave those copies pointing into a recycled chunk (the NO_STACK_COVER
 * corruption family); the pin keeps the object in place for the frame's
 * lifetime. Treated as a bare slot by the major's mark; skipped (already
 * pinned by the minor's pre-pass) by evacuation. map[0] == -2 can never
 * begin a real ref-map program (their [0] is the total length >= 1). */
const int32_t _emperor_gc_pin_refmap[1] = { -2 };

static GCScanRegion* _emperor_gc_scan_regions = NULL;
static size_t _emperor_gc_scan_region_count = 0;
static size_t _emperor_gc_scan_region_capacity = 0;

/* base -> index hash index over _emperor_gc_scan_regions. Containers
 * register/unregister their buffers on every grow/dispose; with tens of
 * thousands of live containers a linear _emperor_gc_scan_remove dominated
 * compiler runtimes (O(regions) per dispose, O(n^2) overall). The map keeps
 * lookup/update O(1); entries move when the array swap-removes, which the
 * remove path re-syncs. */
typedef struct {
    void* key;    /* NULL = empty slot, TOMBSTONE = deleted */
    size_t value; /* index into _emperor_gc_scan_regions */
} GCRegionMapEntry;
#define GC_REGION_TOMBSTONE ((void*)1)

static GCRegionMapEntry* _emperor_gc_region_map = NULL;
static size_t _emperor_gc_region_map_capacity = 0; /* power of two, 0 until first grow */
static size_t _emperor_gc_region_map_used = 0;     /* live + tombstone slots */

static size_t gc_region_map_index(void* p, size_t mask) {
    uintptr_t h = ((uintptr_t)p >> 4) * 0x9E3779B97F4A7C15ULL;
    return (size_t)h & mask;
}

static void gc_region_map_grow(void) {
    size_t new_capacity = _emperor_gc_region_map_capacity ? _emperor_gc_region_map_capacity * 2 : 256;
    GCRegionMapEntry* fresh = (GCRegionMapEntry*)malloc(new_capacity * sizeof(GCRegionMapEntry));
    if (!fresh) return; /* keep the old table; lookups fall back to linear scan */
    memset(fresh, 0, new_capacity * sizeof(GCRegionMapEntry));
    size_t new_mask = new_capacity - 1;
    for (size_t i = 0; i < _emperor_gc_region_map_capacity; i++) {
        void* k = _emperor_gc_region_map[i].key;
        if (k == NULL || k == GC_REGION_TOMBSTONE) continue;
        size_t j = gc_region_map_index(k, new_mask);
        while (fresh[j].key) j = (j + 1) & new_mask;
        fresh[j].key = k;
        fresh[j].value = _emperor_gc_region_map[i].value;
    }
    free(_emperor_gc_region_map);
    _emperor_gc_region_map = fresh;
    _emperor_gc_region_map_capacity = new_capacity;
    _emperor_gc_region_map_used = _emperor_gc_scan_region_count;
}

/* O(1) base -> current array index, or (size_t)-1 when absent (or when the
 * hash index is unavailable due to a failed grow). */
static size_t gc_region_map_find(void* base) {
    if (_emperor_gc_region_map_capacity == 0) return (size_t)-1;
    size_t mask = _emperor_gc_region_map_capacity - 1;
    size_t i = gc_region_map_index(base, mask);
    while (_emperor_gc_region_map[i].key != NULL) {
        if (_emperor_gc_region_map[i].key == base) return _emperor_gc_region_map[i].value;
        i = (i + 1) & mask;
    }
    return (size_t)-1;
}

static void gc_region_map_put(void* base, size_t index) {
    if (_emperor_gc_region_map_capacity == 0 ||
        (_emperor_gc_region_map_used + 1) * 10 > _emperor_gc_region_map_capacity * 7) {
        gc_region_map_grow();
        if (_emperor_gc_region_map_capacity == 0) return; /* first grow failed */
    }
    size_t mask = _emperor_gc_region_map_capacity - 1;
    size_t i = gc_region_map_index(base, mask);
    size_t reuse = (size_t)-1;
    while (_emperor_gc_region_map[i].key != NULL) {
        if (_emperor_gc_region_map[i].key == base) {
            _emperor_gc_region_map[i].value = index; /* index moved (swap-remove) */
            return;
        }
        if (_emperor_gc_region_map[i].key == GC_REGION_TOMBSTONE && reuse == (size_t)-1)
            reuse = i;
        i = (i + 1) & mask;
    }
    if (reuse != (size_t)-1) i = reuse;
    else _emperor_gc_region_map_used++;
    _emperor_gc_region_map[i].key = base;
    _emperor_gc_region_map[i].value = index;
}

static void gc_region_map_del(void* base) {
    if (_emperor_gc_region_map_capacity == 0) return;
    size_t mask = _emperor_gc_region_map_capacity - 1;
    size_t i = gc_region_map_index(base, mask);
    while (_emperor_gc_region_map[i].key != NULL) {
        if (_emperor_gc_region_map[i].key == base) {
            _emperor_gc_region_map[i].key = GC_REGION_TOMBSTONE;
            return;
        }
        i = (i + 1) & mask;
    }
}

void _emperor_gc_scan_add(void* base, size_t bytes) {
    if (!base || bytes == 0) return;
    if (_emperor_gc_scan_region_count == _emperor_gc_scan_region_capacity) {
        size_t new_capacity = _emperor_gc_scan_region_capacity ? _emperor_gc_scan_region_capacity * 2 : 64;
        GCScanRegion* grown = (GCScanRegion*)realloc(_emperor_gc_scan_regions, new_capacity * sizeof(GCScanRegion));
        if (!grown) {
            /* Losing this registration would let the collector sweep every
             * GC object stored in the buffer; there is no safe fallback. */
            fprintf(stderr, "emperor gc: scan-region registry exhausted, aborting\n");
            abort();
        }
        _emperor_gc_scan_regions = grown;
        _emperor_gc_scan_region_capacity = new_capacity;
    }
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].base = (char*)base;
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].bytes = bytes;
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].live_lo = (char*)base;
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].elem_count = 0;
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].elem_stride = 0;
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].elem_map = NULL;
    gc_region_map_put(base, _emperor_gc_scan_region_count);
    _emperor_gc_scan_region_count++;
}

/* Typed buffer registration (phase 3b) — same registry, precise layout. */
void _emperor_gc_track_buffer(void* base, uint64_t count, uint64_t stride,
                              const int32_t* elem_map) {
    if (!base || stride == 0 || !elem_map) return;
    /* Zero the buffer at registration. _malloc returns recycled memory
     * whose capacity tail still holds the arena's previous contents —
     * including stale pointers into the young generation that pass the
     * evacuate path's sanity checks. A minor walking the region would then
     * promote mid-object garbage as if it were a real element (the memcpy
     * of a bogus header smashes the malloc heap — glibc "free(): invalid
     * pointer" under the rewrite mode). NULL words are skipped by the
     * maybe-nursery filter, so [len, cap) reads as empty. Callers register
     * BEFORE any element lands (List._grow / vector / hashmap), so live
     * elements are written after the zeroing. */
    memset(base, 0, (size_t)(count * stride));
    _emperor_gc_scan_add(base, (size_t)(count * stride));
    GCScanRegion* r = &_emperor_gc_scan_regions[_emperor_gc_scan_region_count - 1];
    r->elem_count = count;
    r->elem_stride = stride;
    r->elem_map = elem_map;
}

void _emperor_gc_untrack_buffer(void* base) {
    _emperor_gc_scan_remove(base);
}

void _emperor_gc_scan_remove(void* base) {
    size_t i = gc_region_map_find(base);
    if (i == (size_t)-1) {
        /* No hash index (grow failed) or unregistered base: linear fallback. */
        for (i = 0; i < _emperor_gc_scan_region_count; i++) {
            if (_emperor_gc_scan_regions[i].base == (char*)base) break;
        }
        if (i == _emperor_gc_scan_region_count) return;
    } else if (i >= _emperor_gc_scan_region_count ||
               _emperor_gc_scan_regions[i].base != (char*)base) {
        /* Stale index (should not happen; the map is re-synced on every
         * move) — fall back to a linear scan rather than freeing nothing. */
        for (i = 0; i < _emperor_gc_scan_region_count; i++) {
            if (_emperor_gc_scan_regions[i].base == (char*)base) break;
        }
        if (i == _emperor_gc_scan_region_count) return;
    }
    size_t last = _emperor_gc_scan_region_count - 1;
    if (i != last) {
        _emperor_gc_scan_regions[i] = _emperor_gc_scan_regions[last];
        gc_region_map_put(_emperor_gc_scan_regions[i].base, i);
    }
    gc_region_map_del(base);
    _emperor_gc_scan_region_count--;
}

/* Narrow a registered region's live range (coroutine stacks: live_lo moves to
 * the coroutine's parked sp; raising it past dead frames is the point). */
void _emperor_gc_scan_set_live(void* base, void* live_lo) {
    size_t i = gc_region_map_find(base);
    if (i == (size_t)-1 || i >= _emperor_gc_scan_region_count ||
        _emperor_gc_scan_regions[i].base != (char*)base) {
        for (i = 0; i < _emperor_gc_scan_region_count; i++) {
            if (_emperor_gc_scan_regions[i].base == (char*)base) break;
        }
        if (i == _emperor_gc_scan_region_count) return;
    }
    /* Parked sp values are captured as &marker of a char local
     * (scheduler), so they can be UNALIGNED. The collect scan loop
     * steps (void**)p in 8-byte increments while p < end; with an
     * unaligned low end the final read can start inside the region
     * yet extend past base+bytes — across the mmap boundary into an
     * unmapped page (SIGSEGV). Round DOWN to pointer alignment: a
     * few extra dead bytes scanned is conservative and safe, an
     * overhanging read is fatal. */
    char* lo = (char*)live_lo;
    lo = (char*)((uintptr_t)lo & ~((uintptr_t)sizeof(void*) - 1));
    if (lo < _emperor_gc_scan_regions[i].base) lo = _emperor_gc_scan_regions[i].base;
    _emperor_gc_scan_regions[i].live_lo = lo;
}

/* ---- Precise frame roots ----
 * Emitted code links one EmperorGcFrame per penguin function onto the
 * CURRENT STACK's chain (_emperor_gc_frame_head); the scheduler swaps the
 * head per coroutine (each stack has its own chain) and restores it on
 * sjlj throws. Each slot is either a bare pointer home (map == NULL: *addr
 * is one GC reference — ref/string register, `this` receiver) or an inline
 * struct home (map != NULL: walk the struct AT addr with the ref-map —
 * value-class/enum register, byval aggregate parameter). In this phase the
 * chains are ADDITIONAL roots on top of the conservative scan; GC_VERIFY
 * reports the conservative-vs-precise delta so the switchover (phase 3)
 * happens with evidence. */

typedef struct EmperorGcSlot {
    void* addr;               /* the alloca (or byval param) address */
    const int32_t* map;       /* NULL = *addr is a single ref; else ref-map of the struct at addr */
} EmperorGcSlot;

typedef struct EmperorGcFrame {
    struct EmperorGcFrame* prev;
    int32_t slot_count;
    EmperorGcSlot slots[];    /* [slot_count] entries, built at function entry */
} EmperorGcFrame;

void* _emperor_gc_frame_head = NULL;

/* Set by _emperor_gc_alloc when the threshold is crossed; drained by the
 * __gc_poll calls the emitter places at every call/alloc site. C-side
 * allocations never collect inline in poll mode — collection happens at
 * penguin safepoints where the frame chains are authoritative. */
int _emperor_gc_want_collect = 0;

/* EMPEROR_GC_MODE=conservative: the legacy behavior — allocations collect
 * inline (mid-C, conservative stack scan), polls are no-ops.
 * EMPEROR_GC_MODE=precise: polls collect with precise roots ONLY (frame
 * chains + globals + regions; no conservative stack scan) — the phase-3
 * switchover experiment; a real missed root turns into a wrong free (the
 * suites crash), a merely-dead local does not.
 * The DEFAULT (env unset) IS PRECISE since GC v2 tuned: the generational
 * collector beat the old poll-full-collect default 2× on the compiler
 * self-compile and 1.9× on the LSP recompile workload with both suites
 * green. EMPEROR_GC_MODE=default (or "legacy") opts back into the old
 * non-generational poll-full-collect behavior for bisection. */
static int _emperor_gc_mode_conservative = 0;
/* Green tea (GC v3): the span heap (gc_span.c) + single-generation
 * non-moving collect driver below. Opt-in via EMPEROR_GC_MODE=greentea
 * (M2); the emitted ABI is untouched. */
int _gc_greentea = 0;

/* ---- Fresh-object quarantine ----
 * A poll can fire while a freshly allocated object is still
 * reachable ONLY from an unhomed SSA temporary — the nested-new shape
 * `this.f = new Outer(new Inner(...))`: the Inner result is a call argument
 * with no slot and no heap home until the statement completes, yet polls sit
 * between the two allocations. Frame chains cannot see it, so marking would
 * sweep it mid-statement and the later store would capture a dangling
 * pointer (compiler pass2 crash: IRFunction+parameters List freed between
 * `new IRFunction` and `new IRBuilder(func)`).
 *
 * The cover is a bounded RING of the most recent allocations: anything in
 * SSA/C flight at a collect is at most a few dozen blocks old (the current
 * statement's temps plus a handful of C locals), so 512 slots span every
 * real in-flight window. Bounding matters because an unbounded "everything
 * allocated since the last collection" retention interacts with the
 * threshold growth heuristic on churn-heavy workloads (most young blocks
 * are already-dead string temporaries; retaining them makes every cycle
 * look low-garbage, the threshold quadruples, and collections stop —
 * measured 5.6GB vs 1.1GB peak RSS on the compiler self-compile). Blocks
 * older than the ring are judged by reachability alone: they are from
 * completed statements, so they are homed or garbage. The conservative
 * fallback's inline collects scan the stack and find SSA temps
 * themselves. */
#define GC_QUARANTINE_RING 512
static void* _emperor_gc_quarantine[GC_QUARANTINE_RING];
static size_t _emperor_gc_quarantine_count = 0; /* used slots, <= RING */
static size_t _emperor_gc_quarantine_next = 0;  /* next write position */

static void gc_quarantine_push(void* user) {
    if (!_gc_greentea) return;
    _emperor_gc_quarantine[_emperor_gc_quarantine_next] = user;
    _emperor_gc_quarantine_next = (_emperor_gc_quarantine_next + 1) % GC_QUARANTINE_RING;
    if (_emperor_gc_quarantine_count < GC_QUARANTINE_RING) {
        _emperor_gc_quarantine_count++;
    }
}

/* ---- Generational nursery ----
 * Precise mode + !EMPEROR_GC_NOGEN allocates SMALL objects (< GC_YOUNG_MAX)
 * from bump chunks: 64KB slices cut from 1MB mmap'd superchunks. Allocation
 * is pointer-increment (no malloc, no list insert, no index append — those
 * stay old-generation-only). A minor collection promotes every surviving
 * young object into the old-generation malloc heap and returns the chunks
 * wholesale; nothing young is ever on _emperor_gc_allocation_list or in the
 * sorted index until promoted.
 *
 * Chunk header lives in the slice's first 64 bytes; objects bump from
 * base upward, 8-byte aligned, using the SAME 24B GCHeader layout as the
 * old generation (next stays NULL — chunk walk is size-stepped; marked is
 * the minor-cycle "promoted or pinned" bit; during a promoted object's
 * afterlife size == -1 marks the forwarding tombstone and the first 8 body
 * bytes hold the new address). */


#define GC_CHUNK_SLICE   65536
#define GC_CHUNK_HDR     128 /* must cover sizeof(YoungChunk) — the per-chunk
                              * owner-index fields grew the struct past 64 */
#define GC_SUPER_BYTES   (1024 * 1024)
#define GC_CHUNKS_PER_SUPER (GC_SUPER_BYTES / GC_CHUNK_SLICE)
#define GC_YOUNG_MAX     16384 /* >= this object size: old-gen directly */


/* Budget sweep on the compiler self-compile (repro: EmperorPenguinLib):
 * 16MB→368s, 32MB→197s, 64MB→143s, 128MB→86s, 256MB→73s (RSS ~2x budget).
 * Minor cost is dominated by the conservative cover + region scans
 * (proportional to the LIVE set, not the budget), so frequency drops
 * translate nearly linearly — 128MB is the knee. Supers are malloc'd on
 * demand, so small programs never touch the budget. Since v3.1 the budget
 * also serves the unified heap goal as its ADDITIVE churn slack (§9.2):
 * goal = max(min_heap, factor x live) + EMPEROR_GC_YOUNG. */
static size_t _gc_young_budget = 128 * 1024 * 1024; /* EMPEROR_GC_YOUNG */


static size_t _gc_pin_total = 0;

/* ---- Collector-cost attribution (EMPEROR_GC_STATS=1) ----
 * atexit summary: collection counts per kind, cumulative wall time per
 * phase, allocation totals, peak live bytes. Answers "where do a mode's
 * seconds go" (the default-vs-baseline regression hunt) without a
 * profiler: fulls includes its sweep, genfull includes its nested minor.
 *
 * GC_PROFILE=1 (the `make gc-bench` harness format): one single-line,
 * machine-parseable summary at exit — fulls/minors/genfulls, cumulative
 * MARK and SWEEP phases (majors only) and the total GC wall time, plus
 * allocation counts and the live-peak. Timing counters ride the same
 * _gc_timing gate as EMPEROR_GC_STATS, so unset env is a couple of loads. */
static int _gc_stats_on = 0;
static int _gc_profile_on = 0;
int _gc_timing = 0;                        /* gc_internal.h: gc_span.c charges allocs */
static size_t _gc_st_fulls = 0, _gc_st_minors = 0, _gc_st_genfulls = 0;
static uint64_t _gc_st_ns_full = 0, _gc_st_ns_minor = 0, _gc_st_ns_genfull = 0;
static uint64_t _gc_st_ns_sweep = 0, _gc_st_ns_mark = 0;
size_t _gc_st_allocs = 0, _gc_st_alloc_bytes = 0; /* gc_internal.h */
static size_t _gc_st_live_peak = 0;
static uint64_t _gc_st_polls = 0;

/* Monotonic nanoseconds without pulling winpthread into every program
 * link: llvm-mingw maps clock_gettime to clock_gettime64 (a winpthread
 * DLL export), and plain program/LSP link lines carry no -lwinpthread.
 * QPC is the native monotonic clock there. */
static uint64_t _gc_st_ns_now(void) {
#if defined(_WIN32)
    static LARGE_INTEGER freq;
    LARGE_INTEGER counter;
    if (freq.QuadPart == 0) QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&counter);
    return (uint64_t)((unsigned __int128)counter.QuadPart * 1000000000ull /
                      (unsigned __int128)freq.QuadPart);
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t)ts.tv_sec * 1000000000ull + (uint64_t)ts.tv_nsec;
#endif
}

static void _gc_st_note_live(void) {
    size_t live = _emperor_gc_total_allocated +
                  (_gc_greentea ? (size_t)gt_heap_bytes() : 0);
    if (live > _gc_st_live_peak) _gc_st_live_peak = live;
}

static void _gc_st_report(void) {
    /* Machine-parseable single line for the gc-bench harness. */
    if (_gc_profile_on) {
        fprintf(stderr,
            "gc_profile: fulls=%zu minors=%zu genfulls=%zu "
            "mark_ms=%.3f sweep_ms=%.3f gc_total_ms=%.3f "
            "allocs=%zu alloc_bytes=%zu live_peak_bytes=%zu\n",
            _gc_st_fulls, _gc_st_minors, _gc_st_genfulls,
            _gc_st_ns_mark / 1e6, _gc_st_ns_sweep / 1e6,
            (_gc_st_ns_full + _gc_st_ns_minor + _gc_st_ns_genfull) / 1e6,
            _gc_st_allocs, _gc_st_alloc_bytes, _gc_st_live_peak);
    }
    if (!_gc_stats_on) return;
    fprintf(stderr,
        "gc stats: fulls=%zu (%.3fs, of which sweep %.3fs) minors=%zu (%.3fs) "
        "genfulls=%zu (%.3fs)\n",
        _gc_st_fulls, _gc_st_ns_full / 1e9, _gc_st_ns_sweep / 1e9,
        _gc_st_minors, _gc_st_ns_minor / 1e9,
        _gc_st_genfulls, _gc_st_ns_genfull / 1e9);
    fprintf(stderr,
        "gc stats: allocs=%zu (%.2f MiB) live_peak=%.2f MiB polls=%llu\n",
        _gc_st_allocs, _gc_st_alloc_bytes / 1048576.0,
        _gc_st_live_peak / 1048576.0,
        (unsigned long long)_gc_st_polls);
}











/* Young-allocation fast path: bump within the current chunk. Slow path
 * (fresh chunk / budget / emergency minor) lives below gc_minor. */
static void* gc_resolve_any(void* candidate);


/* ---- Meta pinned objects ----
 * Compile-time value-template OBJECT arguments (MetaEngine "object" kind,
 * M6-step2 4c) cross the meta boundary as raw i64 addresses: baked into the
 * generated stub .ll text and stored in List<i64> template-arg
 * environments. Integers are invisible to the precise marker (and to any
 * ref-map walk), so the object they point at would be swept while still
 * semantically live. The engine pins such addresses for the process
 * lifetime — a compile is a short-lived process and the pin count is
 * bounded by distinct meta-evaluated object values. Pinning a non-pointer
 * i64 is a harmless no-op (resolve finds no block at that address). */
static void** _emperor_gc_pinned = NULL;
static size_t _emperor_gc_pinned_count = 0;
static size_t _emperor_gc_pinned_capacity = 0;

void _emperor_gc_pin_object(long long ref) {
    if (!ref) return;
    if (_emperor_gc_pinned_count == _emperor_gc_pinned_capacity) {
        size_t new_capacity = _emperor_gc_pinned_capacity ? _emperor_gc_pinned_capacity * 2 : 64;
        void** grown = (void**)realloc(_emperor_gc_pinned, new_capacity * sizeof(void*));
        if (!grown) return; /* pin lost — over-retention risk only under OOM */
        _emperor_gc_pinned = grown;
        _emperor_gc_pinned_capacity = new_capacity;
    }
    _emperor_gc_pinned[_emperor_gc_pinned_count++] = (void*)(intptr_t)ref;
}

/* ---- Phase-3 diagnostics (env-gated, zero cost when unset) ----
 * EMPEROR_GC_STRESS_EVERY=N: every Nth poll collects regardless of the want
 * flag — makes rare precise-root misses (wrong frees) reproduce in one run.
 */
static unsigned _emperor_gc_stress_every = 0;
static unsigned _emperor_gc_stress_counter = 0;
static unsigned _emperor_gc_stress_max = 0;    /* stop stressing after N collects */
static void* _emperor_gc_last_collect_site = NULL;
static size_t _emperor_gc_collect_count = 0;
/* Debug interposition support (free_watch.so): only the sweep's Phase C may
 * free() tracked malloc blocks — a dispose_mem freeing one is a misjudged
 * death caught in the act. */
int _emperor_gc_in_sweep = 0;

/* scheduler.c: iterate every coroutine's SAVED frame-chain head (parked
 * stacks); the running stack's head is _emperor_gc_frame_head itself. */
extern void _emperor_sched_each_frame_head(void (*visit)(void*));

static void _emperor_gc_mark_object(void* obj);
static void gc_refmap_walk(const int32_t* m, int32_t idx, char* base,
                           char* root, int root_size,
                           const char* type_name, int depth);

/* Forensics: which collector phase is walking (set around every refmap walk
 * entry point; printed by gc_refmap_abort). */
static const char* _gc_walk_ctx = "?";

static void gc_mark_frame_chain(void* head) {
    for (EmperorGcFrame* f = (EmperorGcFrame*)head; f; f = f->prev) {
        for (int32_t i = 0; i < f->slot_count; i++) {
            const EmperorGcSlot* s = &f->slots[i];
            if (s->map == NULL || s->map == _emperor_gc_pin_refmap) {
                /* Resolve before marking: a bare slot may legitimately hold
                 * a non-tracked pointer (a value-class receiver pointing at
                 * the caller's stack storage, a function value) — writing a
                 * mark through an unresolved address would corrupt it. A
                 * bare slot may also hold the exact base of an object the
                 * minor pinned IN PLACE in a demoted chunk — chunk-aware.
                 * Pin-marker slots mark like bare (the target never moved). */
                void* owner = gc_resolve_any(*(void**)s->addr);
                if (owner) {
                    _emperor_gc_mark_object(owner);
                }
            } else {
                /* Entry-block alloca: its map's offsets are within the struct
                 * by construction — the size cap only guards corruption. */
                _gc_walk_ctx = "frame-mark";
                gc_refmap_walk(s->map, 1, (char*)s->addr, (char*)s->addr,
                               0x40000000, "gcframe", 0);
            }
        }
    }
}

/* ---- GC Mark ----
 * Iterative mark with an explicit worklist: the old recursive marker could
 * blow the C stack on long linked structures (compiler IR lists). An object
 * is marked when pushed, so each node enters the worklist at most once. */

static GCHeader** _emperor_gc_mark_stack = NULL;
static size_t _emperor_gc_mark_stack_capacity = 0;
size_t _emperor_gc_mark_stack_top = 0; /* gc_internal.h: gt_drain checks it */

/* Set when a worklist push fails mid-marking. Marking is then partial, so the
 * collection must NOT sweep (an unmarked live object would be freed); the
 * collector retains everything that cycle instead. Shared with gc_span.c
 * (the green-tea queue/rep growth failures set it too). */
int _emperor_gc_mark_failed = 0;

static int gc_mark_stack_push(GCHeader* h) {
    if (_emperor_gc_mark_stack_top == _emperor_gc_mark_stack_capacity) {
        size_t new_capacity = _emperor_gc_mark_stack_capacity ? _emperor_gc_mark_stack_capacity * 2 : 256;
        GCHeader** grown = (GCHeader**)realloc(_emperor_gc_mark_stack, new_capacity * sizeof(GCHeader*));
        if (!grown) return 0;
        _emperor_gc_mark_stack = grown;
        _emperor_gc_mark_stack_capacity = new_capacity;
    }
    _emperor_gc_mark_stack[_emperor_gc_mark_stack_top++] = h;
    return 1;
}

/* ---- Precise ref-map walking ----
 * See emperor_types.h for the program encoding. The walk visits exactly the
 * pointer locations the emitter declared for the type; anything else in the
 * object body is not a reference and never consulted. mark_failed aborts the
 * recursion early (partial marking retains everything that cycle).
 *
 * Two modes (gc_walk_evacuate): MARK (default) resolves each slot's target
 * and marks it through the mark stack; EVACUATE (minor collection) rewrites
 * young-generation slots in place (promote-or-follow-forwarding) and never
 * marks anything. */

static int _emperor_gc_verify = 0;
/* Typed-buffer origin (phase 3b): the walk's slots may be UNINITIALIZED
 * garbage (container capacity tails), so a young candidate must be
 * owner-resolved to an exact base before it is trusted — precise-object
 * slots skip this by the emission contract. */
static int _gc_walk_typed = 0;

static void* gc_resolve_any(void* candidate) {
    void* span_owner = gt_slot_owner(candidate);
    if (span_owner) return span_owner;
    return gc_resolve_block(candidate);
}



/* The sorted-index pending append (shared by malloc-path allocation and
 * promotion) — see the incremental index note above gc_refresh_sorted. */
static void gc_pending_append(void* user) {
    if (_emperor_gc_sorted_stale) return;
    /* No-GC runs never read the index (no collection ever resolves an
     * interior pointer) — appending grows it to every block ever allocated:
     * pure realloc/memcpy waste (measured >1% of the whole no-GC self-compile
     * plus GBs of RSS). */
    if (_emperor_gc_disabled) return;
    if (_emperor_gc_pending_count == _emperor_gc_pending_capacity) {
        size_t new_capacity = _emperor_gc_pending_capacity ? _emperor_gc_pending_capacity * 2 : 1024;
        void** grown = (void**)realloc(_emperor_gc_pending, new_capacity * sizeof(void*));
        if (!grown) { _emperor_gc_sorted_stale = 1; return; }
        _emperor_gc_pending = grown;
        _emperor_gc_pending_capacity = new_capacity;
    }
    _emperor_gc_pending[_emperor_gc_pending_count++] = user;
}



static void gc_refmap_abort(const char* what, const int32_t* m, const char* type_name, int32_t idx) {
    fprintf(stderr,
            "emperor gc: CORRUPT REF-MAP (%s) in type %s map=%p node=%d ctx=%s — aborting\n",
            what, type_name ? type_name : "?", (const void*)m, (int)idx, _gc_walk_ctx);
    abort();
}

/* Forensics for the size/map mismatch family: dump the walked root's header,
 * residence (nursery chunk / demoted / malloc old-gen), and the map program
 * head, so an abort names WHAT was walked and HOW BIG it claims to be. */
static void gc_walk_forensics(const int32_t* m, char* root, int root_size) {
    fprintf(stderr, "emperor gc: WALK-FORENSICS root=%p root_size=%d", (void*)root, root_size);
    {
        GCHeader* mh = (GCHeader*)(root - sizeof(GCHeader));
        fprintf(stderr, " home=%s hdr(size=%d is_string=%d marked=%d)",
                gt_slot_owner(root) ? "span" : "malloc",
                mh->size, mh->is_string, mh->marked);
    }
    if (m) {
        fprintf(stderr, " map=[");
        for (int i = 0; i < 12 && i < m[0]; i++) fprintf(stderr, "%d ", m[i]);
        fprintf(stderr, "]");
    }
    fprintf(stderr, " body=[");
    for (int i = 0; i < 8 && (i + 1) * 8 <= root_size + 8; i++) {
        fprintf(stderr, "%llx ", (unsigned long long)((void**)root)[i]);
    }
    fprintf(stderr, "]\n");
}

static EMPEROR_NO_ASAN void gc_refmap_walk(const int32_t* m, int32_t idx, char* base,
                                           char* root, int root_size,
                                           const char* type_name, int depth) {
    if (_emperor_gc_mark_failed) return;
    int32_t total = m[0];
    if (depth > 256 || idx < 1 || idx >= total) {
        gc_refmap_abort("index out of bounds", m, type_name, idx);
    }
    int32_t kind = m[idx];
    if (kind == 1) { /* SLOTS: 1, n, (byte_off, sub)*n */
        int32_t n = m[idx + 1];
        if (n < 0 || (int64_t)idx + 2 + 2 * (int64_t)n > (int64_t)total) {
            gc_refmap_abort("slots extent", m, type_name, idx);
        }
        for (int32_t i = 0; i < n; i++) {
            if (_emperor_gc_mark_failed) return;
            int32_t off = m[idx + 2 + 2 * i];
            int32_t sub = m[idx + 3 + 2 * i];
            if (off < 0 || (int32_t)(base - root) + off + (int32_t)sizeof(void*) > root_size) {
                fprintf(stderr, "emperor gc: failing slot off=%d sub=%d sub-struct at +%td\n",
                        off, sub, base - root);
                gc_walk_forensics(m, root, root_size);
                gc_refmap_abort("slot offset outside object", m, type_name, idx);
            }
            if (sub == -1) {
                void* candidate = *(void**)(base + off);
                if (_gc_greentea) {
                    /* One span lookup (resolve+gray merged); the fallback
                     * goes straight to the malloc index — the nursery
                     * chunks and the span re-check below are generational/
                     * dead paths in this mode. */
                    if (gt_mark_candidate(candidate)) continue;
                    void* owner = gc_resolve_block(candidate);
                    if (owner) {
                        GCHeader* child = (GCHeader*)((char*)owner - sizeof(GCHeader));
                        if (!child->marked) {
                            child->marked = 1;
                            if (!gc_mark_stack_push(child)) { _emperor_gc_mark_failed = 1; return; }
                        }
                    }
                    continue;
                }
                void* owner = gt_slot_owner(candidate);
                if (!owner) {
                    owner = gc_resolve_block(candidate);
                    if (owner && _emperor_gc_verify && owner != candidate) {
                        /* Interior old-gen ref (e.g. a leaked enum-payload
                         * address sitting in a container slot). Known to
                         * occur on the compiler workload; REPORT (bounded)
                         * instead of aborting — the abort killed GC_VERIFY
                         * runs before the missed-edge differential (the
                         * actual bug finder) could report. */
                        static int interior_reports = 0;
                        if (interior_reports < 20) {
                            interior_reports++;
                            fprintf(stderr,
                                    "emperor gc: GC_VERIFY: interior pointer in malloc block ref of %s (%p inside %p)\n",
                                    type_name ? type_name : "?", candidate, owner);
                        }
                    }
                }
                if (owner) {
                    GCHeader* child = (GCHeader*)((char*)owner - sizeof(GCHeader));
                    if (!child->marked) {
                        child->marked = 1;
                        if (!gc_mark_stack_push(child)) { _emperor_gc_mark_failed = 1; return; }
                    }
                }
            } else if (sub != 0) {
                gc_refmap_walk(m, sub, base + off, root, root_size, type_name, depth + 1);
            }
        }
    } else if (kind == 2) { /* ENUM: 2, n, sub*n — payload inline at +16 */
        int32_t n = m[idx + 1];
        if (n < 0 || (int64_t)idx + 2 + (int64_t)n > (int64_t)total) {
            gc_refmap_abort("enum extent", m, type_name, idx);
        }
        int64_t tag = *(int64_t*)(base + 8);
        if (tag < 0 || tag >= n) {
            /* Stored tags are member discriminants; the builder sizes the
             * table past the largest one, so landing outside means decoding
             * or memory corruption — EXCEPT in a typed-buffer walk, where
             * the element may be uninitialized capacity-tail garbage (the
             * same tolerance the SLOTS branch's owner resolution applies:
             * nothing readable there, nothing to keep alive). */
            if (_gc_walk_typed) return;
            gc_refmap_abort("enum tag outside table", m, type_name, (int32_t)tag);
        }
        int32_t sub = m[idx + 2 + tag];
        if (sub != 0) {
            gc_refmap_walk(m, sub, base + 16, root, root_size, type_name, depth + 1);
        }
    } else {
        gc_refmap_abort("unknown node kind", m, type_name, idx);
    }
}

/* Verify mode: types still taking the conservative body-scan fallback (no
 * ref-map) are expected to be foreign metadata only; warn once per name so an
 * emitter gap shows up in test logs instead of as silent over-retention. */
static void gc_verify_warn_mapless(const char* name) {
    static const char* warned[64];
    static int warned_count = 0;
    if (!name) return;
    for (int i = 0; i < warned_count; i++) {
        if (strcmp(warned[i], name) == 0) return;
    }
    if (warned_count < 64) warned[warned_count++] = name;
    fprintf(stderr, "emperor gc: GC_VERIFY: type %s has no ref-map (conservative fallback)\n", name);
}

/* obj must be a validated tracked pointer (or NULL). Young-generation
 * objects (nursery chunks or demoted pinned chunks) share the header layout;
 * a major mark reaches them only AFTER the minor that emptied the nursery,
 * so a forwarded tombstone here means a stale slot — follow the forwarding. */
/* Scan ONE object's body: precise ref-map walk when the metadata declares
 * one, else the conservative whole-body word scan (foreign metadata, or a
 * freshly allocated block between zeroing and metaptr stamping — necessary
 * because a field can be a struct that *contains* pointers without the
 * field itself being a single pointer, e.g. `Option<T>` lays out as
 * { ptr metadata, i32 tag, ptr payload }). The child leaf is mode-aware:
 * span-resident children gray their slot (green tea, batched in gt_drain),
 * malloc children mark+push the shared worklist. Shared by gc_mark_drain
 * (large path) and the green-tea span/representative scans (gc_span.c). */
void gc_scan_object_body(GCHeader* cur) {
    if (cur->is_string) return;

    char* user = (char*)cur + sizeof(GCHeader);
    EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
    const int32_t* map = meta ? meta->refmap : NULL;
    if (map) {
        /* Precise walk: only the declared pointer locations are read. */
        _gc_walk_ctx = "mark-drain";
        gc_refmap_walk(map, 1, user, user, cur->size,
                       meta ? meta->name : NULL, 0);
        return;
    }

    if (_emperor_gc_verify && meta) {
        gc_verify_warn_mapless(meta->name);
    }

    void** ptr = (void**)user;
    size_t word_count = (size_t)cur->size / sizeof(void*);
    for (size_t i = 0; i < word_count; i++) {
        void* candidate = ptr[i];
        if (_gc_greentea) {
            /* One span lookup; the fallback is the malloc index directly —
             * gc_resolve_any would re-run the (already failed) span check
             * on every garbage word. */
            if (gt_mark_candidate(candidate)) continue;
            void* owner = gc_resolve_block(candidate);
            if (owner) {
                GCHeader* child = (GCHeader*)((char*)owner - sizeof(GCHeader));
                if (!child->marked) {
                    child->marked = 1;
                    if (!gc_mark_stack_push(child)) { _emperor_gc_mark_failed = 1; return; }
                }
            }
            continue;
        }
        void* owner = gc_resolve_any(candidate);
        if (owner) {
            GCHeader* child = (GCHeader*)((char*)owner - sizeof(GCHeader));
            if (!child->marked) {
                child->marked = 1;
                if (!gc_mark_stack_push(child)) { _emperor_gc_mark_failed = 1; return; }
            }
        }
    }
}

/* Drain the mark worklist. Every child pushed by gc_refmap_walk's mark path
 * (typed-region elements, frame-chain struct slots) sits on the stack
 * WITHOUT its body being walked — only this loop expands a marked object's
 * fields into marked children. The root phases that push via the ref-map
 * walker must be followed by a drain, or the pushed object survives while
 * its referents are judged dead (the FuncParamTypes.param_types List
 * corruption: dispose_mem zeroed buf while find_func_param_types still
 * walked it). Also drained interleaved by gt_drain (green tea). */
void gc_mark_drain(void) {
    while (_emperor_gc_mark_stack_top > 0) {
        GCHeader* cur = _emperor_gc_mark_stack[--_emperor_gc_mark_stack_top];
        gc_scan_object_body(cur);
    }
}

static EMPEROR_NO_ASAN void _emperor_gc_mark_object(void* obj) {
    if (!obj || _emperor_gc_mark_failed) return;
    /* Green tea: span-resident objects take the batched bitmap path — the
     * slot grays and gt_drain scans it later; the header is untouched
     * (colors are span-side). MUST precede the malloc tail: a span header
     * interpreted as a malloc block would mark a header the bitmap sweep
     * never reads, and the object would die while "marked". */
    if (_gc_greentea && gt_gray_object(obj)) return;
    GCHeader* header = (GCHeader*)((char*)obj - sizeof(GCHeader));
    if (header->marked) return;
    header->marked = 1;
    if (!gc_mark_stack_push(header)) { _emperor_gc_mark_failed = 1; return; }
    gc_mark_drain();
}

static EMPEROR_NO_ASAN void _emperor_gc_mark_conservative(void* stack_bottom, void* stack_top) {
    void** ptr = (void**)stack_top;
    while (ptr < (void**)stack_bottom) {
        void* candidate = *ptr;
        void* owner = gc_resolve_any(candidate);
        if (owner) {
            _emperor_gc_mark_object(owner);
        }
        ptr++;
    }
}

/* ---- GC Sweep ---- */

/* Run the class finalizer of a dead object, if any. Non-string objects carry
 * an EmperorClassMetadata* at offset 0 (stamped by codegen right after
 * allocation; a freshly allocated, not-yet-stamped object is still zeroed and
 * skipped via the NULL check). Classes implementing IMemoryDispose have
 * dispose_mem in the destructor slot — void(void*), exactly the slot's
 * signature — releasing raw malloc'd buffers (Array<T,N>.buf, ...) that the
 * collector cannot see. */
/* gc_internal.h: gc_span.c's dead-slot finalizer probe calls this. */
void _emperor_gc_finalize(GCHeader* h) {
    if (h->is_string) return;
    if (h->size < (int)sizeof(void*)) return;
    void* user = (char*)h + sizeof(GCHeader);
    EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
    if (meta && meta->destructor) {
        meta->destructor(user);
    }
}

/* Sweep pass 1 (Phase A+B): unlink dead blocks from the allocation list
 * (tombstoning them marked==2) and run every finalizer while all bodies —
 * dead span slots included — are still allocated and readable. Split out
 * of the old monolithic sweep so the greentea driver can interleave the
 * SPAN finalizer pass here: every finalizer, both heaps, must observe
 * every dead object intact. Returns the pending (dead) chain for pass 2. */
static void gc_sweep_pass1(GCHeader** pending_out) {
    GCHeader* pending = NULL;
    GCHeader** prev = &_emperor_gc_allocation_list;
    GCHeader* curr = _emperor_gc_allocation_list;
    while (curr) {
        if (curr->marked) {
            curr->marked = 0;
            prev = &curr->next;
            curr = curr->next;
        } else {
            GCHeader* dead = curr;
            *prev = curr->next;
            curr = curr->next;
            dead->marked = 2;
            dead->next = pending;
            pending = dead;
        }
    }
    /* Phase B: finalizers run while every dead object is still allocated, so
     * a container finalizer may safely touch the (also dead) objects it owns
     * — e.g. HashMap.dispose_mem reads and disposes its inner Vectors before
     * their own finalizers run. dispose_mem must be idempotent (buf != 0
     * guards in the stdlib containers); a second dispose is a no-op.
     * Allocations made inside a finalizer land on the live list — the
     * collecting flag suppresses nested collection, they get judged next
     * cycle. Their marked stays 0 (fresh), so the compaction below keeps
     * them out of consideration (they are not in the index anyway). */
    for (GCHeader* d = pending; d; d = d->next) {
        _emperor_gc_finalize(d);
    }
    *pending_out = pending;
}

/* Sweep pass 2: compact the sorted index, then free the dead chain. Runs
 * no user code; headers stay readable until the frees (tombstone == 2 is
 * the compaction filter), and control never returns to the mutator in
 * between, so no new allocation can recycle a just-freed address before
 * the index drops it. */
static size_t gc_sweep_pass2(GCHeader* pending) {
    size_t freed = 0;
    {
        size_t w = 0;
        for (size_t r = 0; r < _emperor_gc_sorted_count; r++) {
            GCHeader* h = (GCHeader*)((char*)_emperor_gc_sorted[r] - sizeof(GCHeader));
            if (h->marked != 2) _emperor_gc_sorted[w++] = _emperor_gc_sorted[r];
        }
        _emperor_gc_sorted_count = w;
    }
    _emperor_gc_in_sweep = 1;
    while (pending) {
        GCHeader* dead = pending;
        pending = pending->next;
        freed += sizeof(GCHeader) + dead->size;
        free(dead);
    }
    _emperor_gc_in_sweep = 0;
    return freed;
}

static size_t _emperor_gc_sweep(void) {
    GCHeader* pending = NULL;
    gc_sweep_pass1(&pending);
    return gc_sweep_pass2(pending);
}

/* ---- GC Collect ---- */

/* Non-zero while a collection (including its finalizer phase) is running:
 * blocks re-entry from user finalizers calling gc_collect(), and makes
 * finalizer-triggered allocations defer the next automatic collection. */
static int _emperor_gc_collecting = 0;

/* Non-zero for an EXPLICIT collect (penguin `gc_collect()`, gc_torture):
 * "collect now" semantics. In precise mode such a cycle drops the
 * fresh-object quarantine (young garbage must die — tests assert finalizers
 * ran and gc_info() dropped) and runs the conservative stack scan instead,
 * which is what keeps any in-flight SSA temporaries alive through it. */
static int _emperor_gc_explicit_cycle = 0;

/* ---- Region scanning (shared by major mark and minor evacuation) ----
 * Typed regions walk element slots precisely; conservative regions word-scan
 * (mark for a major — non-moving, safe; PIN for a minor — a conservative
 * word cannot be rewritten, so its young target must keep its address). */

static void gc_scan_regions_mark(void* raw_co_sp) {
    extern int _emperor_gc_on_coroutine;
    for (size_t r = 0; r < _emperor_gc_scan_region_count; r++) {
        GCScanRegion* reg = &_emperor_gc_scan_regions[r];
        if (reg->elem_map) {
            _gc_walk_typed = 1;
            _gc_walk_ctx = "typed-major";
            for (uint64_t i = 0; i < reg->elem_count; i++) {
                gc_refmap_walk(reg->elem_map, 1, reg->base + i * reg->elem_stride,
                               reg->base + i * reg->elem_stride,
                               (int32_t)reg->elem_stride, "typedbuf", 0);
            }
            _gc_walk_typed = 0;
            continue;
        }
        char* lo = reg->live_lo;
        if (lo < reg->base) lo = reg->base;
        char* raw_lo = (char*)raw_co_sp;
        char* rend = reg->base + reg->bytes;
        if (_emperor_gc_on_coroutine &&
            raw_lo >= reg->base && raw_lo < rend && raw_lo < lo) {
            lo = raw_lo;
        }
        /* Same alignment guarantee as _emperor_gc_scan_set_live: every load
         * must stay word-aligned within [lo, rend). */
        lo = (char*)((uintptr_t)lo & ~((uintptr_t)sizeof(void*) - 1));
        char** p = (char**)lo;
        char** end = (char**)rend;
        for (; p < end; p++) {
            void* candidate = *p;
            void* owner = gc_resolve_any(candidate);
            if (owner) {
                _emperor_gc_mark_object(owner);
            }
        }
    }
}



/* ---- Dirty-card scan (minor Phase C) ----
 * The card-table counterpart of the old remembered-slot walk. For every
 * dirty card [lo, lo+512): demoted chunks overlapping the card contribute
 * their objects, malloc old-gen blocks overlapping it contribute
 * themselves; each contributing object gets one gc_evacuate_body walk
 * (ref-map slots rewritten precisely, mapless bodies conservatively
 * pinned). A 512B card can straddle a 64KB chunk-slice boundary and an
 * object can start in the card's predecessor and span into it — both the
 * chunk walk (per-chunk sorted objs[] array) and the malloc walk (sorted
 * index) include the spanning predecessor. Cards that resolve to neither
 * a chunk nor a block (stack allocas that slipped past the barrier's
 * old-target check, false-positive super-range hits) are dropped. */






/* ---- Nursery chunk supply ---- */




/* Chunk-walk helper: the base of the object containing address c. */
/* ---- Minor collection ----
 * Surviving young objects are PROMOTED into the old-generation malloc heap
 * (first-survival promotion: the workload is bimodal — emit_line strings die
 * young, bound trees live forever — so aging would only add copies). Objects
 * seen only through conservative words (coroutine stacks, the explicit /
 * emergency main-stack cover, untyped container buffers) are PINNED: they
 * keep their address and their whole chunk is demoted to old-gen retention
 * (space waste bounded by the pinned objects; freed when the process exits).
 * Everything else dies with its chunk — zero sweep cost for the nursery. */



/* ---- Generational full collection: minor + old-gen mark/sweep ---- */


/* ---- Green-tea (span heap) collection driver (GC v3) ----
 * Single-generation, non-moving, stop-the-world full collection. The root
 * inventory is IDENTICAL to the generational major above (globals, meta
 * pins, frame chains incl. parked coroutines, typed + conservative
 * regions, quarantine, conservative main-stack cover) — only the heap side
 * changes: small objects live in spans (gc_span.c), large objects on the
 * unchanged malloc list. M1 marking is per-object over spans (the shared
 * mark stack + GCHeader.marked); the span-batched marker is M2.
 *
 * The conservative cover is ALWAYS on: in a non-moving collector a
 * conservative hit is merely an extra mark — no pin/demote machinery is
 * needed (that is what lets v3 delete the v2 pin machine at M4).
 *
 * Sweep order (the load-bearing part): malloc pass1 (unlink + finalize;
 * dead bodies intact) -> span finalizer pass (dead malloc bodies still
 * intact, un-freed) -> malloc pass2 (index compact + free) -> span
 * reclaim (clear bits, repool). Every finalizer — either heap — observes
 * every dead object intact, and finalizer-time allocations (malloc path,
 * collecting latch) are never visited by this cycle's unlink walk. */
static void gc_collect_greentea(int emergency) {
    (void)emergency; /* the driver always runs the conservative cover */
    if (_emperor_gc_disabled) return;
    if (!_emperor_gc_stack_bottom || _emperor_gc_collecting) return;
    _emperor_gc_collecting = 1;
    _emperor_gc_collect_count++;
    uint64_t _gc_st_t0 = 0;
    if (_gc_timing) { _gc_st_t0 = _gc_st_ns_now(); _gc_st_fulls++; }

    jmp_buf _gc_register_buf;
    setjmp(_gc_register_buf);
    __asm__ volatile("" ::: "memory");
    void* raw_co_sp = _emperor_gc_get_stack_pointer();

    /* Large-object interior resolution needs the sorted index (span
     * objects resolve O(1) without it). Refresh failure retains
     * everything this cycle, as in the other drivers. */
    if (!gc_refresh_sorted()) {
        _emperor_gc_collecting = 0;
        return;
    }

    for (int i = 0; i < _emperor_gc_global_root_count; i++) {
        void* obj = *(void**)_emperor_gc_global_roots[i];
        _emperor_gc_mark_object(obj);
    }
    for (size_t i = 0; i < _emperor_gc_pinned_count; i++) {
        void* owner = gc_resolve_any(_emperor_gc_pinned[i]);
        if (owner) {
            _emperor_gc_mark_object(owner);
        }
    }
    gc_mark_frame_chain(_emperor_gc_frame_head);
    _emperor_sched_each_frame_head(gc_mark_frame_chain);
    gc_scan_regions_mark(raw_co_sp);
    gc_mark_drain();

    /* Quarantine (fresh-object cover): mirrors gc_collect_main's precise
     * handling — explicit cycles DROP the ring (collect-now semantics; the
     * conservative cover below holds in-flight temps), auto cycles consume
     * it. The mark-failed path keeps it: no sweep ran, the cover carries
     * over. */
    if (_emperor_gc_explicit_cycle) {
        _emperor_gc_quarantine_count = 0;
        _emperor_gc_quarantine_next = 0;
    } else {
        for (size_t i = 0; i < _emperor_gc_quarantine_count; i++) {
            _emperor_gc_mark_object(_emperor_gc_quarantine[i]);
        }
        _emperor_gc_quarantine_count = 0;
        _emperor_gc_quarantine_next = 0;
    }

    /* Conservative main-stack cover (see the driver comment). */
    {
        extern void* _emperor_gc_main_watermark;
        extern int _emperor_gc_on_coroutine;
        void* cover_top = raw_co_sp;
        if (_emperor_gc_on_coroutine && _emperor_gc_main_watermark) {
            cover_top = _emperor_gc_main_watermark;
        }
        _emperor_gc_mark_conservative(_emperor_gc_stack_bottom, cover_top);
    }

    /* Green-tea drain: the span FIFO queue (word-differential bitmap
     * scans), the representative fast path, interleaved with the shared
     * large-object worklist. Ends with gray == black globally. */
    gt_drain();
    if (_emperor_gc_verify) gt_verify_invariants();

    if (_emperor_gc_mark_failed) {
        /* Partial marking happened — nothing may be swept this cycle. */
        _emperor_gc_mark_failed = 0;
        _emperor_gc_mark_stack_top = 0;
        for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) h->marked = 0;
        gt_reset_marks();
        _emperor_gc_collecting = 0;
        return;
    }

    uint64_t _gc_st_mk = 0; /* mark end / sweep start (profile phases) */
    if (_gc_timing) _gc_st_mk = _gc_st_ns_now();
    GCHeader* dead_large = NULL;
    gc_sweep_pass1(&dead_large);
    gt_sweep_finalize();
    size_t freed = gc_sweep_pass2(dead_large);
    gt_sweep_reclaim();
    if (_gc_timing) {
        _gc_st_ns_sweep += _gc_st_ns_now() - _gc_st_mk;
        _gc_st_ns_mark += _gc_st_mk - _gc_st_t0;
    }
    _emperor_gc_total_allocated -= freed;

    /* The unified dual-heap goal (spec §9.2): one budget recalculated from
     * the COMBINED live/freed numbers of both heaps — replaces the old
     * pair of a malloc-side threshold and gt_update_goal. */
    gc_update_unified_goal(freed);
    _emperor_gc_collecting = 0;
    _emperor_gc_want_collect = 0;
    if (_gc_st_t0) { _gc_st_ns_full += _gc_st_ns_now() - _gc_st_t0; _gc_st_note_live(); }
}

void gc_collect_greentea_emergency(void) {
    gc_collect_greentea(1);
}

/* ---- Unified dual-heap goal (spec §9.2) ----
 * The two side-local budgets this replaces each sized themselves to their
 * OWN live set (malloc: 2x malloc-live; span: max(4MiB, 2x span-live)) —
 * but every cycle marks BOTH heaps at O(total live), so whichever budget
 * was smaller dictated the cadence while the cycle still paid the big
 * side's cost. The bootstrap profile: malloc-live ~40MiB vs span-live
 * ~250MiB → the malloc side collected every ~87MiB (59 fulls) and each
 * one walked the 290MiB combined live set; v2 did 10 majors on the same
 * load. One goal over the combined heap restores v2's economics:
 * max(min_heap, factor x total_live) + young — see gc_update_unified_goal. */

int gc_unified_goal_reached(void) {
    return _emperor_gc_total_allocated + (size_t)gt_heap_bytes() >=
           _gc_unified_goal;
}

uint64_t gc_unified_goal_value(void) {
    return (uint64_t)_gc_unified_goal;
}

void gc_update_unified_goal(size_t malloc_freed) {
    size_t live = _emperor_gc_total_allocated + (size_t)gt_live_bytes();
    size_t freed = malloc_freed + (size_t)gt_last_freed();
    /* goal = max(min_heap, factor x live) + young: the live-proportional
     * base PLUS one young budget of guaranteed churn slack between
     * collections — the direct translation of v2's economics (old-gen
     * 2x-live threshold + the nursery's fixed amortization budget). The
     * slack term is what a RAMPING live set needs: a pure factor x live
     * goal pins the garbage fraction near 1/(1+factor) every cycle, so
     * the collector runs at the ramp integral (bootstrap: 5.13GiB of
     * churn under a 0 -> 280MiB live ramp = 47-59 fulls, each marking
     * the whole live set). With the slack, the per-cycle churn budget is
     * live + young, the count drops to ~O(log), and peak heap stays
     * bounded at (1+factor) x live + young. The slack also SUBSUMES the
     * M3 garbage-rich doubling (its job — restoring nursery amortization
     * for churn shapes like mixed512 — is now the goal's floor), and a
     * measured doubling on top of the slack overshot peak heap to
     * ~4x live on ramp-then-churn profiles (bootstrap RSS 1.8GiB), so
     * no rich branch exists. */
    size_t live_target = live * _gc_goal_factor;
    if (live_target < _gc_goal_min_heap) live_target = _gc_goal_min_heap;
    live_target += gc_young_budget_cap();
    if (live_target > _gc_unified_goal) {
        _gc_unified_goal = live_target;
    } else if (freed < live / 4) {
        /* Live-heavy (the LSP recompile shape): freeing almost nothing
         * while each cycle costs O(live blocks) — grow x4, capped. */
        if (_gc_unified_goal < (1ULL << 42)) {
            _gc_unified_goal *= 4;
        }
    }
}

void gc_set_goal_params(size_t factor, size_t min_heap) {
    if (factor) _gc_goal_factor = factor;
    if (min_heap) _gc_goal_min_heap = min_heap;
    /* The same slack-inclusive base gc_update_unified_goal computes, so
     * the knobs take effect before the first collection. */
    size_t base = _gc_goal_min_heap + gc_young_budget_cap();
    if (_gc_unified_goal < base) _gc_unified_goal = base;
}

/* gc_internal.h: the young allocation budget — since v3.1 the unified
 * heap goal's ADDITIVE churn slack on top of the factor x live base
 * (§9.2); historically the v2 nursery's allocation budget. */
size_t gc_young_budget_cap(void) {
    return _gc_young_budget;
}

static EMPEROR_NO_ASAN void gc_collect_main(void) {
    if (_emperor_gc_disabled) return;
    if (!_emperor_gc_stack_bottom || _emperor_gc_collecting) return;
    /* Green tea (GC v3): the span-heap driver replaces the whole cycle. */
    if (_gc_greentea) {
        gc_collect_greentea(0);
        return;
    }
    /* Conservative mode: the inline sequential full collect below. */
    _emperor_gc_collecting = 1;
    _emperor_gc_collect_count++;
    uint64_t _gc_st_t0 = 0;
    if (_gc_timing) { _gc_st_t0 = _gc_st_ns_now(); _gc_st_fulls++; }

    /* Spill ALL registers (especially callee-saved ones: rbx, rbp, r12-r15 on
     * x86-64) onto the stack before scanning. A conservative GC that only walks
     * the stack misses any live pointer that is sitting in a register at the
     * moment of collection — those objects then get swept, their memory reused,
     * and the program observes heap corruption (e.g. a SourceLocation.filename
     * pointer that now reads as a numeric pointer value, or a BoundType* whose
     * bits are ASCII from a reused string buffer). setjmp's jmp_buf is a local
     * in this frame, so it lies within [stack_top, stack_bottom) and the scan
     * below covers the spilled register words. This is the standard
     * register-flush technique used by conservative collectors (e.g. Boehm GC).
     * The volatile asm barrier stops the compiler from optimizing the setjmp
     * away or reordering the stack-pointer read above it. */
    jmp_buf _gc_register_buf;
    setjmp(_gc_register_buf);
    __asm__ volatile("" ::: "memory");

    void* raw_co_sp = _emperor_gc_get_stack_pointer();
    void* stack_top = raw_co_sp;

    /* Coroutine scheduler interop (scheduler.c): when a collection triggers on
     * a coroutine's mmap'd stack, the raw stack pointer belongs to a different
     * memory region and [main_bottom, sp) would span unrelated mappings. Scan
     * the main stack only up to the watermark recorded at the last switch
     * (with a setjmp register flush, so main's callee-saved registers are on
     * its stack and covered); the coroutine's own stack — including every
     * other coroutine's — is a registered scan region below. */
    extern void* _emperor_gc_main_watermark;
    extern int _emperor_gc_on_coroutine;
    if (_emperor_gc_on_coroutine && _emperor_gc_main_watermark) {
        stack_top = _emperor_gc_main_watermark;
    }

    /* Resolve interior pointers to their owning block during marking (see
     * gc_resolve_block). If the sorted index cannot be refreshed (allocation
     * failure in the full-rebuild fallback), retain everything this cycle
     * instead of risking a partial marking. */
    if (!gc_refresh_sorted()) {
        _emperor_gc_collecting = 0;
        return;
    }

    for (int i = 0; i < _emperor_gc_global_root_count; i++) {
        void* obj = *(void**)_emperor_gc_global_roots[i];
        _emperor_gc_mark_object(obj);
    }

    /* Meta pinned objects (see the pinning note above): marked every cycle
     * in every mode — the baked i64 holders are not ref-mapped anywhere.
     * Resolve first: a pinned value may be a plain integer (most #fun
     * returns are), and marking straight through it would read a header at
     * (int - 24). */
    for (size_t i = 0; i < _emperor_gc_pinned_count; i++) {
        void* owner = gc_resolve_block(_emperor_gc_pinned[i]);
        if (owner) {
            _emperor_gc_mark_object(owner);
        }
    }

    /* Precise frame chains: the running stack's head plus every PARKED
     * coroutine's saved head (the running coroutine's saved value is stale —
     * its frames are linked on the current head — so the scheduler's walker
     * skips it; phase 2: additional roots alongside the conservative scans
     * below, GC_VERIFY reports the delta between the two root sets). */
    gc_mark_frame_chain(_emperor_gc_frame_head);
    _emperor_sched_each_frame_head(gc_mark_frame_chain);

    /* Registered raw buffers (container element storage) and coroutine
     * stacks: scan the LIVE portion of each region like an extension of the
     * stack so GC references inside stay live. Coroutine regions are narrowed
     * to their parked sp (scheduler); the RUNNING coroutine's region (the one
     * containing the raw stack pointer) is capped at the collect-time sp so
     * only its live frames — never its own dead slots below sp — are roots,
     * exactly like the sequential main-stack model this replaces. */
    for (size_t r = 0; r < _emperor_gc_scan_region_count; r++) {
        char* lo = _emperor_gc_scan_regions[r].live_lo;
        if (lo < _emperor_gc_scan_regions[r].base) lo = _emperor_gc_scan_regions[r].base;
        char* raw_lo = (char*)raw_co_sp;
        char* rbase = _emperor_gc_scan_regions[r].base;
        char* rend = rbase + _emperor_gc_scan_regions[r].bytes;
        if (_emperor_gc_on_coroutine &&
            raw_lo >= rbase && raw_lo < rend && raw_lo < lo) {
            lo = raw_lo;
        }
    /* Same alignment guarantee as _emperor_gc_scan_set_live: every load
     * must stay word-aligned within [lo, rend). */
        lo = (char*)((uintptr_t)lo & ~((uintptr_t)sizeof(void*) - 1));
        char** p = (char**)lo;
        char** end = (char**)rend;
        for (; p < end; p++) {
            void* candidate = *p;
            void* owner = gc_resolve_block(candidate);
            if (owner) {
                _emperor_gc_mark_object(owner);
            }
        }
    }

    /* GC_VERIFY differential (conservative mode): everything marked so far (globals +
     * frame chains + regions — the phase-3 precise root set) is restamped
     * 3; the conservative stack scan below then marks the remaining
     * reachable objects 1. Objects left at 1 are what the precise net would
     * MISS — the number that must reach "only explainable strays" before
     * phase 3 drops the conservative scan. Reported once per type name.
     * Precise mode skips the scan entirely (and the differential). */
    int verify_phase = _emperor_gc_verify && !_gc_greentea;
    if (verify_phase) {
        for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) {
            if (h->marked == 1) h->marked = 3;
        }
    }

    _emperor_gc_mark_conservative(_emperor_gc_stack_bottom, stack_top);

    if (verify_phase) {
        static const char* reported[128];
        static int reported_count = 0;
        size_t conservative_only = 0;
        static GCHeader* miss_set[8192];
        size_t miss_n = 0;
        for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) {
            if (h->marked != 1) continue;
            conservative_only++;
            if (miss_n < 8192) miss_set[miss_n++] = h;
            if (!h->is_string && h->size >= (int)sizeof(void*)) {
                EmperorClassMetadata* meta = *(EmperorClassMetadata**)((char*)h + sizeof(GCHeader));
                const char* name = meta ? meta->name : NULL;
                int seen = 0;
                for (int i = 0; i < reported_count; i++) {
                    if ((name == NULL && reported[i] == NULL) ||
                        (name != NULL && reported[i] != NULL && strcmp(name, reported[i]) == 0)) {
                        seen = 1;
                        break;
                    }
                }
                if (!seen && reported_count < 128) {
                    reported[reported_count++] = name;
                    fprintf(stderr,
                            "emperor gc: GC_VERIFY: conservative-only retention of %s "
                            "(precise roots would miss it — frame-slot gap?)\n",
                            name ? name : "(anonymous)");
                }
            }
            h->marked = 3; /* keep it: verify is diagnostic, never destructive */
        }
        if (conservative_only > 0) {
            /* Diagnostics: the misses above are often TRANSITIVE (a stack
             * word roots A, A's ref-map reaches them; the real gap is A's
             * slot). Report the stack words that point INTO the miss set,
             * plus the live frame chain, so the gap can be attributed to a
             * specific function's frame. */
            int printed = 0;
            static char* miss_text_lo;
            static char* miss_text_hi;
            if (!miss_text_lo) {
                FILE* maps = fopen("/proc/self/maps", "r");
                if (maps) {
                    char line[512];
                    uintptr_t here = (uintptr_t)&_emperor_gc_collect;
                    while (fgets(line, sizeof(line), maps)) {
                        uintptr_t lo, hi;
                        char perms[8];
                        if (sscanf(line, "%llx-%llx %7s",
                                   (unsigned long long*)&lo,
                                   (unsigned long long*)&hi, perms) == 3 &&
                            here >= lo && here < hi && strchr(perms, 'x')) {
                            miss_text_lo = (char*)lo;
                            miss_text_hi = (char*)hi;
                            break;
                        }
                    }
                    fclose(maps);
                }
                if (!miss_text_lo) miss_text_lo = miss_text_hi = (char*)1;
            }
            char* text_lo = miss_text_lo;
            char* text_hi = miss_text_hi;
            for (char** p = (char**)stack_top;
                 p < (char**)_emperor_gc_stack_bottom && printed < 12; p++) {
                void* owner = gc_resolve_block(*p);
                if (!owner) continue;
                for (size_t i = 0; i < miss_n; i++) {
                    if ((char*)owner == (char*)miss_set[i] + sizeof(GCHeader)) {
                        /* Strings have no metadata header — name by flag. */
                        const char* mname = "?";
                        if (miss_set[i]->is_string) {
                            mname = "string";
                        } else {
                            EmperorClassMetadata* meta = *(EmperorClassMetadata**)owner;
                            if (meta && meta->name) mname = meta->name;
                        }
                        /* Attribute the word to a frame: the owning frame's
                         * descriptor is the closest one at or below the word;
                         * "slot-start" tells uncovered-alloca apart from a
                         * slot that exists but didn't get marked. */
                        EmperorGcFrame* own = NULL;
                        int is_slot_start = 0;
                        for (EmperorGcFrame* f = (EmperorGcFrame*)_emperor_gc_frame_head;
                             f; f = f->prev) {
                            if ((char*)f < (char*)stack_top ||
                                (char*)f >= (char*)_emperor_gc_stack_bottom) break;
                            if ((char*)f <= (char*)p &&
                                (!own || (char*)f > (char*)own)) {
                                own = f;
                            }
                            for (int32_t s = 0; s < f->slot_count; s++) {
                                if (*(void**)&f->slots[s] == (void*)p) is_slot_start = 1;
                            }
                        }
                        fprintf(stderr,
                                "    miss-set stack word at stack_bottom-%td -> %s%s\n",
                                (char*)_emperor_gc_stack_bottom - (char*)p,
                                mname,
                                is_slot_start ? " [IS a slot start]" : "");
                        printed++;
                        if (own && text_lo != (char*)1) {
                            for (char** q = (char**)own;
                                 q < (char**)_emperor_gc_stack_bottom; q++) {
                                char* w = *q;
                                if (w >= text_lo && w < text_hi) {
                                    fprintf(stderr,
                                            "      owner frame@stack_bottom-%td retaddr_off=+0x%lx\n",
                                            (char*)_emperor_gc_stack_bottom - (char*)own,
                                            (unsigned long)(w - text_lo));
                                    break;
                                }
                            }
                        }
                        break;
                    }
                }
            }
            if (printed == 0) {
                fprintf(stderr, "    (no stack word points into the miss set)\n");
            }
            for (EmperorGcFrame* f = (EmperorGcFrame*)_emperor_gc_frame_head; f; f = f->prev) {
                if ((char*)f < (char*)stack_top ||
                    (char*)f >= (char*)_emperor_gc_stack_bottom) {
                    break; /* off-stack guard */
                }
                fprintf(stderr, "    frame@stack_bottom-%td n=%d\n",
                        (char*)_emperor_gc_stack_bottom - (char*)f,
                        f->slot_count);
                if (text_lo != (char*)1) {
                    for (char** q = (char**)f;
                         q < (char**)_emperor_gc_stack_bottom; q++) {
                        char* w = *q;
                        if (w >= text_lo && w < text_hi) {
                            fprintf(stderr, "      retaddr_off=+0x%lx\n",
                                    (unsigned long)(w - text_lo));
                            break;
                        }
                    }
                }
            }
        }
        if (conservative_only > 0) {
            static uint64_t cycles_reported = 0;
            if ((cycles_reported++ & 0x1FF) == 0) { /* every 512nd cycle: progress, not spam */
                fprintf(stderr, "emperor gc: GC_VERIFY: conservative-only retention ongoing (last cycle: %zu object(s))\n",
                        conservative_only);
            }
        }
        /* Normalize back to 1 so the sweep below keeps every marked block. */
        for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) {
            if (h->marked == 3) h->marked = 1;
        }
    }

    if (_emperor_gc_mark_failed) {
        /* Partial marking happened — nothing may be swept this cycle. Reset
         * flags (and the worklist top; stale entries are already-marked
         * headers that a later pass would skip anyway) and retain all. */
        _emperor_gc_mark_failed = 0;
        _emperor_gc_mark_stack_top = 0;
        for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) h->marked = 0;
        _emperor_gc_collecting = 0;
        return;
    }

    uint64_t _gc_st_mk = 0; /* mark end / sweep start (profile phases) */
    if (_gc_timing) _gc_st_mk = _gc_st_ns_now();
    size_t freed;
    if (_gc_timing) {
        freed = _emperor_gc_sweep();
        _gc_st_ns_sweep += _gc_st_ns_now() - _gc_st_mk;
        _gc_st_ns_mark += _gc_st_mk - _gc_st_t0;
    } else {
        freed = _emperor_gc_sweep();
    }
    _emperor_gc_total_allocated -= freed;

    /* Threshold follows the LIVE heap (Boehm-style growth): a workload that
     * allocates many short-lived strings collects >90% garbage every cycle,
     * so the old "double only when little was freed" rule never fired and the
     * threshold stayed at 256KB — a full mark-sweep every 256KB of allocation
     * (thousands of collections over one compiler run, each rebuilding the
     * sorted index and re-scanning the whole live heap). Sizing the threshold
     * to 2x the surviving live set bounds collections to O(total_alloc /
     * live_set) while capping peak memory at ~3x live.
     *
     * The complement case (freed < live/4: the live heap dominates, e.g. the
     * LSP recompiling a large project per keystroke) grows x4 instead of x2:
     * each collection costs O(live blocks) in mark, so collecting every
     * `threshold` bytes while freeing almost nothing is pure overhead. The
     * x4 growth trades peak RSS (live + threshold) for far fewer full scans;
     * capped at 4TB to bound the multiplication. */
    size_t live = _emperor_gc_total_allocated;
    size_t live_target = live * 2;
    if (live_target > _emperor_gc_threshold) {
        _emperor_gc_threshold = live_target;
    } else if (freed < live / 4) {
        if (_emperor_gc_threshold < (1ULL << 42)) {
            _emperor_gc_threshold *= 4;
        }
    }
    _emperor_gc_collecting = 0;
    _emperor_gc_want_collect = 0;
    if (_gc_st_t0) { _gc_st_ns_full += _gc_st_ns_now() - _gc_st_t0; _gc_st_note_live(); }
}

/* Public entry = the penguin `gc_collect()` builtin (and gc_torture):
 * explicit "collect now" semantics — see _emperor_gc_explicit_cycle. */
EMPEROR_NO_ASAN void _emperor_gc_collect(void) {
    _emperor_gc_explicit_cycle = 1;
    gc_collect_main();
    _emperor_gc_explicit_cycle = 0;
}

/* Internal entry for the automatic cycles (safepoint polls' full triggers,
 * stress collects): quarantine cover applies; the conservative main-stack
 * cover rides inside gc_minor/gc_collect_generational via
 * _gc_stack_cover_poll (default on — see its declaration). */
static void gc_collect_auto(void) {
    gc_collect_main();
}

/* ---- GC Init ---- */

void _emperor_gc_init(void* stack_bottom) {
    _emperor_gc_allocation_list = NULL;
    _emperor_gc_total_allocated = 0;
    _emperor_gc_threshold = 256 * 1024;
    _gc_unified_goal = _gc_goal_min_heap;
    _emperor_gc_sorted_count = 0;
    _emperor_gc_pending_count = 0;
    _emperor_gc_sorted_stale = 0;
    _emperor_gc_quarantine_count = 0;
    _emperor_gc_quarantine_next = 0;
    _emperor_gc_pinned_count = 0;
    _emperor_gc_global_root_count = 0;
    _emperor_gc_scan_region_count = 0;
    _emperor_gc_region_map_capacity = 0;
    _emperor_gc_region_map_used = 0;
    if (_emperor_gc_region_map) {
        free(_emperor_gc_region_map);
        _emperor_gc_region_map = NULL;
    }
    _emperor_gc_stack_bottom = stack_bottom;
    {
        const char* st = getenv("EMPEROR_GC_STATS");
        _gc_stats_on = (st != NULL && st[0] != '\0' && st[0] != '0');
        const char* prof = getenv("GC_PROFILE");
        _gc_profile_on = (prof != NULL && prof[0] != '\0' && prof[0] != '0');
        _gc_timing = _gc_stats_on || _gc_profile_on;
        if (_gc_stats_on || _gc_profile_on) atexit(_gc_st_report);
    }
    const char* kill = getenv("EMPEROR_GC_DISABLE");
    _emperor_gc_disabled = (kill != NULL && kill[0] != '\0' && kill[0] != '0');
    if (_emperor_gc_disabled) {
        _emperor_gc_threshold = 8ULL << 40;
        _gc_unified_goal = 8ULL << 40;
    }
    const char* verify = getenv("GC_VERIFY");
    _emperor_gc_verify = (verify != NULL && verify[0] != '\0' && verify[0] != '0');
    const char* mode = getenv("EMPEROR_GC_MODE");
    _emperor_gc_mode_conservative =
        (mode != NULL && mode[0] != '\0' && strcmp(mode, "conservative") == 0);
    /* Green tea (GC v3, DEFAULT): span heap + the driver above. The v2
     * generational collector was deleted at M4b; "precise"/"legacy" now
     * warn and fall back to greentea (bisect with an older tree).
     * "conservative" keeps the pre-v2 inline-collect emergency fallback. */
    _gc_greentea = 1;
    if (mode != NULL && mode[0] != '\0' &&
        (strcmp(mode, "precise") == 0 || strcmp(mode, "legacy") == 0)) {
        fprintf(stderr,
                "emperor gc: EMPEROR_GC_MODE=%s was removed with the v2 "
                "generational collector; using greentea\n", mode);
    }
    const char* young = getenv("EMPEROR_GC_YOUNG");
    if (young != NULL && young[0] != '\0') {
        unsigned long long b = strtoull(young, NULL, 10);
        if (b >= GC_CHUNK_SLICE) _gc_young_budget = (size_t)b;
    }
    const char* stress = getenv("EMPEROR_GC_STRESS_EVERY");
    if (stress != NULL && stress[0] != '\0') {
        _emperor_gc_stress_every = (unsigned)strtoul(stress, NULL, 10);
    }
    const char* stress_max = getenv("EMPEROR_GC_STRESS_MAX");
    if (stress_max != NULL && stress_max[0] != '\0') {
        _emperor_gc_stress_max = (unsigned)strtoul(stress_max, NULL, 10);
    }
    if (_gc_greentea) {
        /* Unified-goal knobs (spec §9.2/§12): factor and min-heap floor. */
        size_t gf = 0, mh = 0;
        const char* gfs = getenv("EMPEROR_GC_GOAL_FACTOR");
        if (gfs != NULL && gfs[0] != '\0') gf = (size_t)strtoull(gfs, NULL, 10);
        const char* mhs = getenv("EMPEROR_GC_MIN_HEAP");
        if (mhs != NULL && mhs[0] != '\0') mh = (size_t)strtoull(mhs, NULL, 10);
        gc_set_goal_params(gf, mh);
    }
}

/* ---- Runtime ABI version ----
 * The emitted code and the C runtime evolve in lockstep (ref-maps, write
 * barriers, typed buffer tracking). Consumers that mix an emission against
 * a foreign runtime — a stale .penguin-lib, a JIT module from another
 * build — must compare this symbol and refuse loudly instead of
 * misbehaving. Bump on every layout/protocol change below. */
/* Bumped at M4c (GC v3): the emitter stopped emitting write-barrier
 * calls, so code emitted by an M4c+ compiler REQUIRES a v3 runtime
 * (barrier-less .ll under a v2 runtime loses remembered-set coverage);
 * symmetrically, old emissions still link against the no-op symbols.
 * Mixed dynlib builds compare this tag and refuse (see above). */
const char* const _emperor_runtime_abi = "emperor-rt-gc3-gt1";

/* ---- GC Info ---- */

uint64_t _emperor_gc_info(void) {
    return (uint64_t)_emperor_gc_total_allocated +
           (_gc_greentea ? gt_heap_bytes() : 0);
}

int _emperor_gc_probe_tracked(void* user) {
    if (gt_slot_owner(user) == user) return 1;
    return gc_resolve_block(user) == user;
}

int _emperor_gc_sweeping(void) {
    return _emperor_gc_in_sweep;
}

uint64_t _emperor_gc_debug_pin_count(void) {
    return (uint64_t)_gc_pin_total;
}

void _emperor_gc_info_split(uint64_t* old_bytes, uint64_t* young_bytes) {
    *old_bytes = (uint64_t)_emperor_gc_total_allocated;
    *young_bytes = (uint64_t)(_gc_greentea ? gt_heap_bytes() : 0);
}

size_t _emperor_gc_alloc_charge(int size) {
    if (_gc_greentea) {
        unsigned slot = gt_slot_size_for(size);
        if (slot) return slot;
    }
    int asize = (size + 7) & ~7;
    if (asize < 8) asize = 8;
    return sizeof(GCHeader) + asize;
}

/* ---- Generational write barriers (see emperor_gc.h) ---- */

/* Write barriers: no-ops since GC v3 — the collector is single-generation
 * and stop-the-world on the only mutator, so no remembered set exists.
 * The symbols stay for ABI compatibility: emissions still carry the calls
 * until the M4c emitter change retires them (and previously emitted .ll
 * keeps linking). */
void _emperor_gc_write_barrier(void* obj, void** slot) {
    (void)obj; (void)slot;
}

void _emperor_gc_write_barrier_map(void* obj, void* slot, const int32_t* map) {
    (void)obj; (void)slot; (void)map;
}

/* ---- GC-tracked Allocation ---- */

void* _emperor_gc_alloc(int size, int is_string) {
    /* Green tea: small objects come from the span heap (one bitmap scan +
     * memset per slot; no list insert, no index append). Never while
     * collecting: finalizer-time allocations take the malloc path below so
     * no sweep in progress can judge them. A NULL return here (OOM after
     * the emergency collect) falls through to the malloc path. */
    if (_gc_greentea && !_emperor_gc_collecting && size <= GT_MAX_BODY) {
        void* small = gt_alloc_small(size, is_string);
        if (small) {
            gc_quarantine_push(small);
            return small;
        }
    }
    int total = (int)sizeof(GCHeader) + size;
    if (_gc_timing) { _gc_st_allocs++; _gc_st_alloc_bytes += (size_t)total; }
    GCHeader* header = (GCHeader*)malloc(total);
    if (!header) return NULL;
    memset(header, 0, total);
    header->next = _emperor_gc_allocation_list;
    header->marked = 0;
    header->is_string = is_string;
    header->size = size;
    _emperor_gc_allocation_list = header;
    _emperor_gc_total_allocated += total;

    /* Register with the incremental sorted index (see gc_refresh_sorted):
     * MUST happen before the threshold collection below so the very
     * collection this allocation may trigger sees the block. On append
     * failure mark the index stale — the next refresh falls back to a full
     * rebuild, and until then appends are skipped (nothing can go missing:
     * the rebuild walks the authoritative list). */
    gc_pending_append((char*)header + sizeof(GCHeader));

    gc_quarantine_push((char*)header + sizeof(GCHeader));

    /* Poll-mode collection scheduling: crossing the goal only RAISES the
     * flag; the __gc_poll calls the emitter places at every penguin
     * call/alloc site drain it. Greentea checks the UNIFIED dual-heap
     * budget (malloc + span bytes vs gc_update_unified_goal's target —
     * see gc_internal.h); conservative mode keeps the legacy malloc-only
     * threshold and its inline-collect behavior exactly.
     *
     * Emergency (both modes): a C-heavy stretch with no poll in sight
     * grows past 8x the goal — collect right here (the conservative scan
     * covers C locals), as the legacy path did.
     *
     * An inline collection here may run BEFORE this pointer reaches the
     * caller, so the fresh block is marked around it (a marked block skips
     * the body scan — safe, the body is still zero-filled) — otherwise the
     * collection its own allocation triggered would sweep it (dangling
     * return → tcache reuse corrupts the header/list). */
    if (!_emperor_gc_disabled && !_emperor_gc_collecting) {
        if (_gc_greentea) {
            if (gc_unified_goal_reached()) {
                if (_emperor_gc_total_allocated + (size_t)gt_heap_bytes() >=
                    _gc_unified_goal * 8) {
                    header->marked = 1;
                    gc_collect_greentea_emergency();
                    header->marked = 0;
                } else {
                    _emperor_gc_want_collect = 1;
                }
            }
        } else if (_emperor_gc_total_allocated >= _emperor_gc_threshold) {
            header->marked = 1;
            gc_collect_auto();
            header->marked = 0;
        }
    }

    return (char*)header + sizeof(GCHeader);
}

/* Safepoint poll: called by emitted code before every call and at every
 * allocation site. Cheap when no collection is pending: one load + ret.
 * greentea: the heap-goal flag runs the span collector's full cycle;
 * conservative mode never polls (allocations collect inline). */
void _emperor_gc_poll(void) {
    if (_gc_timing) _gc_st_polls++;
    if (_emperor_gc_mode_conservative || _emperor_gc_collecting ||
        !_emperor_gc_stack_bottom || _emperor_gc_disabled) {
        return;
    }
    if (_emperor_gc_stress_every) {
        if (_emperor_gc_stress_max && _emperor_gc_collect_count >= _emperor_gc_stress_max) {
            _emperor_gc_stress_every = 0; /* budget exhausted — normal polls */
        } else if (++_emperor_gc_stress_counter >= _emperor_gc_stress_every) {
            _emperor_gc_stress_counter = 0;
            _emperor_gc_last_collect_site = __builtin_return_address(0);
            _emperor_gc_want_collect = 1; /* stress = full collection */
            gc_collect_auto();
        }
        return;
    }
    if (!_emperor_gc_want_collect) return;
    _emperor_gc_last_collect_site = __builtin_return_address(0);
    gc_collect_auto();
}
