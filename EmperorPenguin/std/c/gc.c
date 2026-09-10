/* EmperorPenguin generational garbage collector (GC v2, final design).
 *
 * Heap layout: objects < GC_YOUNG_MAX bump-allocate in 64KB nursery chunks
 * cut from 1MB superchunks; survivors of a minor promote into the malloc
 * old generation (forwarding tombstones at their old addresses); objects
 * seen only through words the collector cannot rewrite are pinned and
 * their chunk page-retained ("demoted"). Old-gen blocks live on a singly
 * linked list with an incrementally maintained sorted index.
 *
 * Roots: per-function frame descriptors (emitted by LLVMEmitter; bare-ref
 * and ref-map struct slots, chain/param pin mirrors), global roots, typed
 * container-buffer regions (#__track_buffer), meta-pinned objects, and the
 * fresh-object quarantine ring.
 *
 * Collection: _emperor_gc_poll (emitted before every call/alloc) drains
 * the young-budget flag with a MINOR (gc_minor: pin pass -> dirty cards ->
 * precise roots -> transitive evacuation -> finalizers -> chunk recycle)
 * and the old-gen threshold with a generational FULL (gc_collect_
 * generational: minor + old mark/sweep + dead-demoted-chunk reclaim).
 * Old->young edges are tracked by a 512B card table written by the emitted
 * barriers. Modes (EMPEROR_GC_MODE): precise/generational is the DEFAULT;
 * "legacy" opts into the non-generational poll-collected heap,
 * "conservative" into the pre-v2 inline-collect behavior.
 *
 * Env knobs (see also emperor_gc.h): GC_VERIFY, EMPEROR_GC_YOUNG,
 * EMPEROR_GC_STATS, EMPEROR_GC_STRESS_EVERY/_MAX, EMPEROR_GC_STACK_COVER /
 * _NO_STACK_COVER, EMPEROR_GC_REGION_PINS, EMPEROR_GC_RS_FALLBACK /
 * _NO_RS_FALLBACK, EMPEROR_GC_NOGEN, EMPEROR_GC_DISABLE.
 *
 * File map: index/resolve (sorted index, chunk index, gc_resolve_any) ->
 * regions -> frame roots -> quarantine -> nursery -> stats -> pinning ->
 * stress -> mark/drain -> ref-map walk -> evacuation -> sweep -> collect
 * driver (gc_collect_main / _auto / _generational) -> region scans ->
 * dirty-card scan -> chunk supply -> minor -> generational full -> init ->
 * barriers -> allocation -> poll.
 */
#include "emperor_types.h"
#include "emperor_gc.h"
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

/* ---- GC Header ---- */

typedef struct GCHeader {
    struct GCHeader* next;
    int marked;
    int is_string;
    int size;
} GCHeader;

static GCHeader* _emperor_gc_allocation_list = NULL;
static size_t _emperor_gc_total_allocated = 0;
static size_t _emperor_gc_threshold = 256 * 1024; /* 256KB initial */

/* Runtime kill-switch: set EMPEROR_GC_DISABLE=1 in the environment to raise
 * the threshold to 8TB so automatic collection never fires (diagnostics —
 * tells GC-induced crashes apart from logic bugs without recompiling).
 * Explicit gc_collect() calls are never gated. */
static int _emperor_gc_disabled = 0;

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

/* Old-generation objects minted since the last gc_refresh_sorted live ONLY
 * in _emperor_gc_pending — gc_resolve_block cannot see them. Every resolve
 * consumer except the write barrier runs inside a collection (after the
 * refresh); the barrier runs BETWEEN collections, so a freshly promoted
 * container must be recognizable or its old-target check fails, the
 * remembered-set entry is skipped, and the next minor judges the container's
 * young field target dead and recycles the chunk under it (the missed
 * StringBuilder-data edge family). Membership set mirroring the pending
 * array, wholesale-cleared wherever the array drains. */
static void** _gc_pending_old_hash = NULL;
static size_t _gc_pending_old_hash_cap = 0;   /* power of two, 0 = empty */
static size_t _gc_pending_old_hash_used = 0;

static int gc_ptr_cmp(const void* a, const void* b) {
    void* pa = *(void* const*)a;
    void* pb = *(void* const*)b;
    return pa < pb ? -1 : (pa > pb ? 1 : 0);
}

static void gc_pending_old_clear(void) {
    /* The slots must go too: resetting only `used` leaves stale entries
     * occupying the probe chains, the load-factor grow condition never
     * fires, and once every slot is non-NULL an insert probes forever
     * (pass2 hung at 100% CPU inside the probe loop). */
    if (_gc_pending_old_hash_cap) {
        memset(_gc_pending_old_hash, 0,
               _gc_pending_old_hash_cap * sizeof(void*));
    }
    _gc_pending_old_hash_used = 0;
}

static void gc_pending_old_add(void* user) {
    if (_gc_pending_old_hash_used * 2 >= _gc_pending_old_hash_cap) {
        size_t new_cap = _gc_pending_old_hash_cap ? _gc_pending_old_hash_cap * 2 : 1024;
        void** fresh = (void**)calloc(new_cap, sizeof(void*));
        if (!fresh) return; /* membership stays partial: a missing entry
                             * only degrades to the pre-fix barrier miss */
        for (size_t i = 0; i < _gc_pending_old_hash_cap; i++) {
            void* e = _gc_pending_old_hash[i];
            if (!e) continue;
            size_t j = ((uintptr_t)e >> 4) & (new_cap - 1);
            while (fresh[j]) j = (j + 1) & (new_cap - 1);
            fresh[j] = e;
        }
        free(_gc_pending_old_hash);
        _gc_pending_old_hash = fresh;
        _gc_pending_old_hash_cap = new_cap;
    }
    size_t i = ((uintptr_t)user >> 4) & (_gc_pending_old_hash_cap - 1);
    while (_gc_pending_old_hash[i]) {
        if (_gc_pending_old_hash[i] == user) return;
        i = (i + 1) & (_gc_pending_old_hash_cap - 1);
    }
    _gc_pending_old_hash[i] = user;
    _gc_pending_old_hash_used++;
}

static int gc_pending_old_contains(void* user) {
    if (!_gc_pending_old_hash_cap) return 0;
    size_t i = ((uintptr_t)user >> 4) & (_gc_pending_old_hash_cap - 1);
    while (_gc_pending_old_hash[i]) {
        if (_gc_pending_old_hash[i] == user) return 1;
        i = (i + 1) & (_gc_pending_old_hash_cap - 1);
    }
    return 0;
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
        gc_pending_old_clear();
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
            gc_pending_old_clear();
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
    gc_pending_old_clear();
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
static int _emperor_gc_mode_precise = 0;

/* ---- Fresh-object quarantine ----
 * In precise mode a poll can fire while a freshly allocated object is still
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
 * completed statements, so they are homed or garbage. Default mode needs
 * none of this: its conservative stack scan finds SSA temps. */
#define GC_QUARANTINE_RING 512
static void* _emperor_gc_quarantine[GC_QUARANTINE_RING];
static size_t _emperor_gc_quarantine_count = 0; /* used slots, <= RING */
static size_t _emperor_gc_quarantine_next = 0;  /* next write position */

static void gc_quarantine_push(void* user) {
    if (!_emperor_gc_mode_precise) return;
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

typedef struct YoungChunk {
    struct YoungChunk* next_active; /* active list (incl. current bump chunk) */
    struct YoungChunk* next_free;   /* free list */
    char* base;                     /* first object byte (after this header) */
    char* top;
    char* end;
    int pinned;                     /* pinned this minor (conservative hit) */
    int demoted;                    /* moved to the old-pinned list: its
                                     * objects are old-gen now, never again
                                     * resolved as young targets */
    int mark_hit;                   /* a root/conservative word resolved into
                                     * this chunk during THIS major's mark —
                                     * possibly via a tombstone whose mark
                                     * forwarded to the promoted copy. Such
                                     * readers still read the chunk's memory,
                                     * so the chunk must not be reclaimed even
                                     * when no live object was marked in it. */
    /* Sorted array of every object base in [base, top): bump allocation
     * only ever appends, so the array is address-ordered by construction —
     * owner resolution (the conservative-scan hot path) binary-searches it
     * instead of stride-walking thousands of headers per hit. NULL when the
     * index allocation failed (the stride walk remains the fallback). */
    char** objs;
    size_t nobjs;
    size_t cobjs;
} YoungChunk;

#define GC_CHUNK_SLICE   65536
#define GC_CHUNK_HDR     128 /* must cover sizeof(YoungChunk) — the per-chunk
                              * owner-index fields grew the struct past 64 */
#define GC_SUPER_BYTES   (1024 * 1024)
#define GC_CHUNKS_PER_SUPER (GC_SUPER_BYTES / GC_CHUNK_SLICE)
#define GC_YOUNG_MAX     16384 /* >= this object size: old-gen directly */

typedef struct YoungSuper {
    struct YoungSuper* next;
} YoungSuper;

static YoungSuper* _gc_supers = NULL;
static YoungChunk* _gc_young_active = NULL;  /* chunks holding young objects */
static YoungChunk* _gc_young_cur = NULL;     /* current bump chunk (may also be in active) */
static YoungChunk* _gc_young_free = NULL;
static YoungChunk* _gc_old_pinned = NULL;    /* demoted pinned chunks (old-gen) */
static size_t _gc_old_pinned_at_major = 0;   /* old_pinned count after the last
                                              * major — the minor-end scheduler
                                              * asks for a major at 2x this */
static size_t _gc_young_chunk_bytes = 0;     /* bytes of active+cur chunks */
/* Budget sweep on the compiler self-compile (repro: EmperorPenguinLib):
 * 16MB→368s, 32MB→197s, 64MB→143s, 128MB→86s, 256MB→73s (RSS ~2x budget).
 * Minor cost is dominated by the conservative cover + region scans
 * (proportional to the LIVE set, not the budget), so frequency drops
 * translate nearly linearly — 128MB is the knee. Supers are malloc'd on
 * demand, so small programs never touch the budget. */
static size_t _gc_young_budget = 128 * 1024 * 1024; /* EMPEROR_GC_YOUNG */
static int _gc_generational = 0;             /* precise && !NOGEN */
static int _gc_in_minor = 0;
static int _gc_want_minor = 0;
/* Address -> chunk lookup index: sorted-by-base array of ALL chunks that can
 * hold objects (active + old-pinned; free chunks hold nothing). Rebuilt on
 * chunk-list changes only (rare), binary-searched per candidate. */
static YoungChunk** _gc_chunk_index = NULL;
static size_t _gc_chunk_index_n = 0;
static size_t _gc_chunk_index_cap = 0;
static int _gc_chunk_index_stale = 1;
static char* _gc_nursery_lo = (char*)-1;
static char* _gc_nursery_hi = (void*)0;
/* Exact superchunk ranges, sorted by start address. The [lo,hi) span can
 * cover unrelated malloc arenas (promoted old-gen blocks and container
 * buffers interleave with the superchunk mmaps address-wise), and every
 * such false positive paid a chunk-index binary search in the conservative
 * scans — the dominant precise-mode cost once marking is complete and the
 * full container working set stays alive. A nursery candidate must fall
 * inside an ACTUAL super. Supers are never freed, so ranges only append. */
static char** _gc_super_ranges = NULL;   /* 2*N: start0, end0, start1, ... */
static size_t _gc_super_range_count = 0;
static size_t _gc_super_range_cap = 0;
/* Card table (write barrier): dirty 512B cards of the old generation.
 * A barrier on an old-object store dirties the card containing the SLOT;
 * the next minor scans only the objects overlapping dirty cards — plus the
 * precise roots' promotion closure — and never walks the whole old
 * generation (that walk stays reserved for RS_FALLBACK / GC_VERIFY).
 *
 * The old generation is not contiguous (promoted malloc blocks + demoted
 * nursery chunks interleave with foreign arenas), so the Java-style
 * heap_base + (addr >> 9) byte array cannot apply: the table is an
 * open-addressed set of card keys (slot & ~(512-1)). Marking is
 * UNCONDITIONAL for old holders — classic card semantics, no value check
 * at the store; the scan judges each field's CURRENT value. That drops the
 * young-value lookup the old slot-precise remembered set paid per store,
 * and page granularity dedups by construction: k stores into one object
 * cost one entry, not k.
 *
 * Cards drain at every minor (scan + clear). An old object only dies
 * inside a MAJOR, and every major runs its minor first, so a dirty card's
 * holder is always alive at scan time and clearing is lossless (re-stores
 * re-mark — unlike one-shot slot records, a card marked once covers every
 * later store into the same page until the next minor). */
#define GC_CARD_BYTES 512
static uintptr_t* _gc_cards = NULL;  /* open-addressed card-key set */
static size_t _gc_cards_cap = 0;     /* power of two, 0 = empty */
static size_t _gc_cards_used = 0;
static size_t _gc_st_cards_marked = 0; /* cumulative mark operations */
static size_t _gc_st_cards = 0;         /* cumulative dirty cards scanned */
/* Old-generation full ref-map scan ("the barrier fallback"): discovers
 * every old->young edge without relying on the write barrier's card table.
 * The default minor never runs it — barriers are emitted for every
 * WRMBR/struct store, container buffers are typed regions, and the C
 * runtime's own ref-field stores are barrier-guarded. GC_VERIFY runs it
 * post-evacuation as the missed-edge detector (any promotion it performs
 * is reported); EMPEROR_GC_RS_FALLBACK=1 forces it on as the pre-root edge
 * source (bisection / barrier-less emissions). */
static int _gc_rs_fallback = 0;

/* Conservative main-stack cover on minors — OFF by default.
 * The emitter homes every pointer-bearing register (parameters and
 * receivers via pin mirrors, wchain/alias/unbox values via chain pins,
 * value-class/enum temps via struct slots), so precise roots own every
 * live reference and a per-minor cover is pure cost: each pass pinned
 * ~10k stale/spill words, demoting a handful of 64KB chunks to permanent
 * page retention (measured 8.9x live/RSS amplification on the
 * self-compile). Explicit collects and the C-side emergency path keep a
 * hardcoded conservative cover (in-flight C/SSA temps are unhomed there);
 * the MAJOR's mark keeps its own always-on stack scan (non-moving).
 * EMPEROR_GC_STACK_COVER=1 restores the per-minor cover (bisection). */
static int _gc_stack_cover_poll = 0;

/* Typed-region (container element) treatment at minors. REWRITE (default):
 * elements evacuate like every other precise slot — an exact young base is
 * promoted and the slot rewritten to the new address; interiors (enum
 * payload aliases) still pin their owner. This keeps the container working
 * set flowing into the compact malloc old generation instead of pinning
 * every element target in place FOREVER: under pins-only, each minor
 * demoted every chunk holding a container-referenced young object and the
 * 64KB page retention accumulated (self-compile: 26k demoted chunks /
 * 1.67GB retained for 146MB of live objects — the dominant RSS term).
 * Safety of the rewrite: region removal strictly precedes buffer free
 * (_grow and dispose_mem both call _gc_scan_remove before _mfree), so a
 * registered region never points at freed memory; the capacity-tail's
 * stale words fail exact-header validation and take the interior path
 * (bounded over-retention, same as pins-only). EMPEROR_GC_REGION_PINS=1
 * restores the old pins-only walk (bisection). */
static int _gc_region_pin_mode = 0;
static size_t _gc_pin_total = 0;
/* Live nursery bytes (info()); promotions counter (GC_VERIFY's
 * missed-edge report measures the delta across a scan pass). */
static size_t _gc_young_bytes = 0;
static size_t _gc_promote_count = 0;

/* ---- Collector-cost attribution (EMPEROR_GC_STATS=1) ----
 * atexit summary: collection counts per kind, cumulative wall time per
 * phase, allocation totals, peak live bytes. Answers "where do a mode's
 * seconds go" (the default-vs-baseline regression hunt) without a
 * profiler: fulls includes its sweep, genfull includes its nested minor. */
static int _gc_stats_on = 0;
static size_t _gc_st_fulls = 0, _gc_st_minors = 0, _gc_st_genfulls = 0;
static uint64_t _gc_st_ns_full = 0, _gc_st_ns_minor = 0, _gc_st_ns_genfull = 0;
static uint64_t _gc_st_ns_sweep = 0;
static size_t _gc_st_allocs = 0, _gc_st_alloc_bytes = 0;
static size_t _gc_st_live_peak = 0;
static uint64_t _gc_st_polls = 0;
static int _gc_in_genfull = 0;

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
                  (_gc_generational ? _gc_young_bytes : 0);
    if (live > _gc_st_live_peak) _gc_st_live_peak = live;
}

static void _gc_st_report(void) {
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
    /* Retention accounting (RSS attribution): demoted chunks are
     * page-retained forever while any live pinned survivor stays in them;
     * their count x 64KB is the floor the pin sources (pin slots, region
     * element pins, quarantine) put under the live heap. */
    {
        size_t demoted = 0, demoted_live_obj = 0;
        for (YoungChunk* c = _gc_old_pinned; c; c = c->next_active) {
            demoted++;
            char* p = c->base;
            while (p < c->top) {
                GCHeader* h = (GCHeader*)p;
                int asize = (h->size + 7) & ~7;
                if (asize < 8) asize = 8;
                if (!h->next && h->marked != 2) demoted_live_obj++;
                p += sizeof(GCHeader) + asize;
            }
        }
        fprintf(stderr,
            "gc stats: demoted_chunks=%zu (%.2f MiB retained, live objs %zu) "
            "young_bytes=%.2f MiB pin_slots=%zu cards=%zu marked=%zu\n",
            demoted, demoted * 64.0 / 1024.0, demoted_live_obj,
            _gc_young_bytes / 1048576.0, _gc_pin_total,
            _gc_st_cards, _gc_st_cards_marked);
    }
}

static size_t gc_card_hash(uintptr_t key, size_t mask) {
    return (size_t)(((key >> 9) * 0x9E3779B97F4A7C15ULL) & (uintptr_t)mask);
}

static void gc_card_mark(void* slot) {
    uintptr_t key = (uintptr_t)slot & ~(uintptr_t)(GC_CARD_BYTES - 1);
    if (_gc_cards_used * 2 >= _gc_cards_cap) {
        size_t new_cap = _gc_cards_cap ? _gc_cards_cap * 2 : 256;
        uintptr_t* fresh = (uintptr_t*)malloc(new_cap * sizeof(uintptr_t));
        if (!fresh) return; /* card lost: GC_VERIFY / RS_FALLBACK cover it */
        memset(fresh, 0, new_cap * sizeof(uintptr_t));
        for (size_t i = 0; i < _gc_cards_cap; i++) {
            uintptr_t k = _gc_cards[i];
            if (!k) continue;
            size_t j = gc_card_hash(k, new_cap - 1);
            while (fresh[j]) j = (j + 1) & (new_cap - 1);
            fresh[j] = k;
        }
        free(_gc_cards);
        _gc_cards = fresh;
        _gc_cards_cap = new_cap;
    }
    size_t mask = _gc_cards_cap - 1;
    size_t i = gc_card_hash(key, mask);
    while (_gc_cards[i]) {
        if (_gc_cards[i] == key) return; /* already dirty */
        i = (i + 1) & mask;
    }
    _gc_cards[i] = key;
    _gc_cards_used++;
    _gc_st_cards_marked++;
}

static void gc_cards_clear(void) {
    if (_gc_cards_cap) {
        memset(_gc_cards, 0, _gc_cards_cap * sizeof(uintptr_t));
    }
    _gc_cards_used = 0;
}

/* Rebuild the address->chunk index (called on chunk-list mutations; lists
 * are tiny — dozens to hundreds of chunks). */
static int gc_chunk_ptr_cmp(const void* a, const void* b) {
    YoungChunk* pa = *(YoungChunk* const*)a;
    YoungChunk* pb = *(YoungChunk* const*)b;
    return pa->base < pb->base ? -1 : (pa->base > pb->base ? 1 : 0);
}

static void gc_chunk_index_refresh(void) {
    size_t n = 0;
    for (YoungChunk* c = _gc_young_active; c; c = c->next_active) n++;
    for (YoungChunk* c = _gc_old_pinned; c; c = c->next_active) n++;
    if (n > _gc_chunk_index_cap) {
        size_t new_cap = n * 2;
        YoungChunk** grown = (YoungChunk**)realloc(_gc_chunk_index, new_cap * sizeof(YoungChunk*));
        if (!grown) { _gc_chunk_index_stale = 1; return; }
        _gc_chunk_index = grown;
        _gc_chunk_index_cap = new_cap;
    }
    size_t w = 0;
    for (YoungChunk* c = _gc_young_active; c; c = c->next_active) _gc_chunk_index[w++] = c;
    for (YoungChunk* c = _gc_old_pinned; c; c = c->next_active) _gc_chunk_index[w++] = c;
    qsort(_gc_chunk_index, w, sizeof(YoungChunk*), gc_chunk_ptr_cmp);
    _gc_chunk_index_n = w;
    _gc_chunk_index_stale = 0;
}

/* Incremental maintenance: the index stays sorted across single-chunk
 * mutations (acquire/recycle/unlink), so hot lookups (every write barrier)
 * never pay a rebuild. Demotion (active -> old_pinned) needs no update —
 * the chunk keeps its address and stays indexed. */
static void gc_chunk_index_insert(YoungChunk* c) {
    if (_gc_chunk_index_n == _gc_chunk_index_cap) {
        size_t new_cap = _gc_chunk_index_cap ? _gc_chunk_index_cap * 2 : 64;
        YoungChunk** grown = (YoungChunk**)realloc(_gc_chunk_index, new_cap * sizeof(YoungChunk*));
        if (!grown) { _gc_chunk_index_stale = 1; return; }
        _gc_chunk_index = grown;
        _gc_chunk_index_cap = new_cap;
    }
    if (_gc_chunk_index_stale) return; /* rebuilt wholesale at next lookup */
    size_t lo = 0, hi = _gc_chunk_index_n;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if (_gc_chunk_index[mid]->base <= c->base) lo = mid + 1;
        else hi = mid;
    }
    memmove(&_gc_chunk_index[lo + 1], &_gc_chunk_index[lo],
            (_gc_chunk_index_n - lo) * sizeof(YoungChunk*));
    _gc_chunk_index[lo] = c;
    _gc_chunk_index_n++;
}

static void gc_chunk_index_remove(YoungChunk* c) {
    if (_gc_chunk_index_stale) return;
    size_t lo = 0, hi = _gc_chunk_index_n;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if (_gc_chunk_index[mid]->base < c->base) lo = mid + 1;
        else hi = mid;
    }
    while (lo < _gc_chunk_index_n && _gc_chunk_index[lo]->base == c->base) {
        if (_gc_chunk_index[lo] == c) {
            memmove(&_gc_chunk_index[lo], &_gc_chunk_index[lo + 1],
                    (_gc_chunk_index_n - lo - 1) * sizeof(YoungChunk*));
            _gc_chunk_index_n--;
            return;
        }
        lo++;
    }
}

/* Address -> owning chunk (object slots resolve to the chunk whose slice
 * contains them), or NULL. Candidate must be 8-aligned. allow_stale: skip
 * the index refresh when it is marked stale — the write barrier must never
 * pay a full index rebuild (qsort over the whole chunk set) between
 * collections; a stale miss only downgrades its old-target check to the
 * malloc-index path, which is conservatively correct. */
static YoungChunk* gc_chunk_of_stale_ok(void* candidate) {
    if (_gc_chunk_index_n == 0) return NULL;
    char* c = (char*)candidate;
    size_t lo = 0, hi = _gc_chunk_index_n;
    while (lo + 1 < hi) {
        size_t mid = lo + (hi + 0 - lo) / 2;
        if (_gc_chunk_index[mid]->base <= c) lo = mid;
        else hi = mid;
    }
    YoungChunk* ch = _gc_chunk_index[lo];
    if (ch->base > c) return NULL;
    if (c < ch->top) return ch; /* objects live in [base, top); the tail is
                                 * either unallocated (active) or finalized
                                 * dead space (pinned) — not a valid target */
    return NULL;
}

static YoungChunk* gc_chunk_of(void* candidate) {
    if (_gc_chunk_index_stale) gc_chunk_index_refresh();
    return gc_chunk_of_stale_ok(candidate);
}

/* Fast range pre-filter for nursery candidates (mmap'd superchunks only:
 * promoted malloc blocks never share these pages). Falls through to an
 * exact super-range lookup so interleaved malloc arenas inside the [lo,hi)
 * span are rejected here instead of inside the chunk-index search. */
static int gc_maybe_nursery(void* p) {
    char* c = (char*)p;
    if (c < _gc_nursery_lo || c >= _gc_nursery_hi) return 0;
    if (!_gc_super_ranges) return 1;
    size_t lo = 0, hi = _gc_super_range_count;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if ((char*)_gc_super_ranges[2 * mid + 1] <= c) lo = mid + 1;
        else hi = mid;
    }
    if (lo >= _gc_super_range_count) return 0;
    return c >= (char*)_gc_super_ranges[2 * lo];
}

/* Young-allocation fast path: bump within the current chunk. Slow path
 * (fresh chunk / budget / emergency minor) lives below gc_minor. */
static void gc_minor(int conservative_cover);
static YoungChunk* gc_chunk_acquire(void);
static void* gc_resolve_any(void* candidate);

static void* gc_alloc_young(int size, int is_string) {
    int asize = (size + 7) & ~7;
    if (asize < 8) asize = 8; /* forwarding pointer must fit the body */
    YoungChunk* c = _gc_young_cur;
    if (!c || c->top + sizeof(GCHeader) + asize > c->end) {
        c = gc_chunk_acquire();
        if (!c) return NULL;
    }
    GCHeader* h = (GCHeader*)c->top;
    c->top += sizeof(GCHeader) + asize;
    memset(h, 0, sizeof(GCHeader) + asize);
    h->is_string = is_string;
    h->size = size;
    _gc_young_bytes += sizeof(GCHeader) + asize;
    if (_gc_stats_on) { _gc_st_allocs++; _gc_st_alloc_bytes += (size_t)(sizeof(GCHeader) + asize); }
    /* Owner-index append (sorted by construction — bump order). */
    if (c->nobjs == c->cobjs) {
        size_t new_cap = c->cobjs ? c->cobjs * 2 : 256;
        char** grown = (char**)realloc(c->objs, new_cap * sizeof(char*));
        if (grown) { c->objs = grown; c->cobjs = new_cap; }
    }
    if (c->nobjs < c->cobjs) {
        c->objs[c->nobjs++] = (char*)h + sizeof(GCHeader);
    }
    gc_quarantine_push((char*)h + sizeof(GCHeader));
    return (char*)h + sizeof(GCHeader);
}

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
static size_t _emperor_gc_mark_stack_top = 0;

/* Set when a worklist push fails mid-marking. Marking is then partial, so the
 * collection must NOT sweep (an unmarked live object would be freed); the
 * collector retains everything that cycle instead. */
static int _emperor_gc_mark_failed = 0;

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
static int _gc_walk_evacuate = 0;
/* Typed-buffer origin (phase 3b): the walk's slots may be UNINITIALIZED
 * garbage (container capacity tails), so a young candidate must be
 * owner-resolved to an exact base before it is trusted — precise-object
 * slots skip this by the emission contract. */
static int _gc_walk_typed = 0;

/* ---- Minor evacuation machine ----
 * A promoted young object leaves a tombstone (size == -1) whose first body
 * word is the forwarding address; a PINNED young object (seen only by
 * conservative words, which cannot be rewritten) stays at its address with
 * marked set and its whole chunk demoted to old-gen retention. Both shapes
 * feed the evacuation worklist so their reference fields get rewritten. */

static void** _gc_evac_stack = NULL;
static size_t _gc_evac_stack_cap = 0;
static size_t _gc_evac_stack_top = 0;
static int _gc_evac_overflow = 0;

static void gc_evac_push(void* user) {
    if (_gc_evac_stack_top == _gc_evac_stack_cap) {
        size_t new_cap = _gc_evac_stack_cap ? _gc_evac_stack_cap * 2 : 256;
        void** grown = (void**)realloc(_gc_evac_stack, new_cap * sizeof(void*));
        if (!grown) { _gc_evac_overflow = 1; return; }
        _gc_evac_stack = grown;
        _gc_evac_stack_cap = new_cap;
    }
    _gc_evac_stack[_gc_evac_stack_top++] = user;
}

/* Resolve a conservative candidate to the young object CONTAINING it (owner
 * base), or NULL. Precise slots skip this — they hold exact bases by the
 * emission contract (GC_VERIFY asserts it); typed buffer slots cannot (the
 * uninitialised capacity tail is arbitrary garbage), so they walk the chunk.
 * A chunk pinned EARLIER IN THIS MINOR still resolves (its unmarked objects
 * must stay discoverable for the conservative chain to propagate through
 * chunk-mates); only DEMOTED chunks (a previous minor's) are excluded. */
static void* gc_owner_in_chunk(void* candidate, YoungChunk* ch) {
    char* c = (char*)candidate;
    if (ch->objs && ch->nobjs) {
        /* Binary search the sorted base array: find the last base <= c, then
         * validate containment via its header (a tombstoned/promoted object
         * keeps a sane size for the stride, so the extent check holds). */
        size_t lo = 0, hi = ch->nobjs;
        while (lo + 1 < hi) {
            size_t mid = lo + (hi - lo) / 2;
            if (ch->objs[mid] <= c) lo = mid;
            else hi = mid;
        }
        if (ch->objs[lo] > c) return NULL;
        GCHeader* bh = (GCHeader*)(ch->objs[lo] - sizeof(GCHeader));
        int bsz = (bh->size + 7) & ~7;
        if (bsz < 8) bsz = 8;
        if (c < ch->objs[lo] + bsz) return ch->objs[lo];
        return NULL;
    }
    char* p = ch->base;
    while (p < ch->top) {
        GCHeader* h = (GCHeader*)p;
        int asize = (h->size + 7) & ~7;
        if (asize < 8) asize = 8;
        char* obj = p + sizeof(GCHeader);
        if (c >= obj && c < obj + asize) return obj;
        p += sizeof(GCHeader) + asize;
    }
    return NULL;
}

static void* gc_young_owner_of(void* candidate) {
    if (!gc_maybe_nursery(candidate)) return NULL;
    YoungChunk* ch = gc_chunk_of(candidate);
    if (!ch || ch->demoted) return NULL;
    return gc_owner_in_chunk(candidate, ch);
}

/* Same walk over active AND demoted chunks: interior-tolerant root handling
 * (below) must keep enum-payload interior references alive, and those owners
 * live in demoted chunks after their first pin. Returns the containing
 * object's base (== candidate for exact bases), or NULL. */
static void* gc_young_owner_of_any(void* candidate) {
    if (!gc_maybe_nursery(candidate)) return NULL;
    YoungChunk* ch = gc_chunk_of(candidate);
    if (!ch) return NULL;
    return gc_owner_in_chunk(candidate, ch);
}

/* Chunk-aware block resolution for the generational MAJOR's root paths.
 * The minor pins conservative-hit objects IN PLACE (their chunk demotes to
 * old-gen retention) and precise bare slots keep pointing at them, so a
 * major's candidate may legitimately live in a nursery/demoted chunk —
 * gc_resolve_block only knows the malloc'd old-gen sorted index and drops
 * such references, marking live chunk objects dead. Resolve chunk
 * interiors first (a promoted object's tombstone forwards to the live
 * malloc copy), then fall back to the malloc index. Chunk memory comes
 * from raw superchunk mallocs that are never tracked, so a chunk-index
 * hit whose object walk misses (the never-allocated tail) owns nothing
 * and must NOT fall through. */
static void* gc_resolve_any(void* candidate) {
    if (gc_maybe_nursery(candidate)) {
        YoungChunk* ch = gc_chunk_of(candidate);
        if (ch) {
            ch->mark_hit = 1;
            void* owner = gc_young_owner_of_any(candidate);
            if (!owner) return NULL;
            GCHeader* yh = (GCHeader*)((char*)owner - sizeof(GCHeader));
            if (yh->next) return (void*)yh->next;
            return owner;
        }
        /* Range pre-filter only: [lo,hi) can span unrelated allocations
         * (the same caveat refmap_walk's resolution applies) — a chunk
         * miss must still consult the malloc index. */
    }
    return gc_resolve_block(candidate);
}

/* Promote one young object into the old-generation malloc heap. Returns the
 * NEW user pointer (or the original address under malloc failure — pinned). */
static void* gc_promote_young(GCHeader* h);
static void gc_pin_young(void* obj);

/* Retain an interior-referenced owner: the referencing word cannot be
 * rewritten, so the owner must survive AT ITS ADDRESS (pin). If the owner
 * was already promoted this cycle (an earlier phase moved it), the best
 * possible rescue is pinning the tombstone's chunk — the stale interior
 * then reads frozen-but-valid memory for the rest of the process. */
static void gc_retain_interior(void* owner) {
    GCHeader* oh = (GCHeader*)((char*)owner - sizeof(GCHeader));
    if (oh->next) {
        YoungChunk* och = gc_chunk_of(owner);
        if (och && !och->demoted) och->pinned = 1;
        return;
    }
    gc_pin_young(owner);
}

/* Precise slot rewrite: young exact base -> promote / forwarding / pinned.
 * Interior-tolerant: RDENUM write-chain aliases legitimately leak enum
 * PAYLOAD addresses (base+16) into bare-ref container slots and frame
 * temporaries; such a word cannot be rewritten, but its OWNER must survive
 * in place — resolve to the containing object and pin it. A forwarded young
 * object is recognizable by next != NULL (young headers never link a list;
 * the field is idle until promotion stores the new USER address there —
 * size stays intact so chunk walks keep their stride).
 * GC_VERIFY rescue context: set by gc_old_scan_young_refs while it walks an
 * old holder, so a promotion triggered from its post-evacuation pass can
 * name the holder + slot (a missed barrier/root locator). */
static int _gc_rescue_reporting = 0;
static const char* _gc_rescue_holder = NULL;
static char* _gc_rescue_holder_base = NULL;
static const char* _gc_rescue_holder_kind = NULL; /* old-malloc / old-pinned */

static void gc_evacuate_slot(void** slot) {
    void* p = *slot;
    if (!p || !gc_maybe_nursery(p)) return;
    if (_gc_walk_typed && _gc_region_pin_mode) {
        /* REGION_PINS bisect mode (the pre-rewrite semantics): PIN the
         * resolved owner, NEVER rewrite the slot. The rewrite mode below
         * replaced this as the default — see its comment. Kept as a
         * one-switch rollback for corruption bisection. */
        YoungChunk* tch = gc_chunk_of(p);
        if (tch && !tch->demoted) {
            void* owner = gc_owner_in_chunk(p, tch);
            if (owner) gc_retain_interior(owner);
        }
        return;
    }
    YoungChunk* ch = gc_chunk_of(p);
    int exact_ok = 0;
    if (ch && (char*)p < ch->top) {
        GCHeader* vh = (GCHeader*)((char*)p - sizeof(GCHeader));
        if (vh->size >= 0 && vh->size <= GC_YOUNG_MAX &&
            vh->is_string >= 0 && vh->is_string <= 1 &&
            (vh->marked >= 0 && vh->marked <= 2)) {
            exact_ok = 1;
        }
    }
    if (!exact_ok) {
        /* Interior / stale word pointing into indexed-chunk space: keep the
         * owning object alive at its address (or a gap / recycled slice —
         * nothing to do). */
        void* owner = gc_young_owner_of_any(p);
        if (owner) gc_retain_interior(owner);
        return;
    }
    GCHeader* h = (GCHeader*)((char*)p - sizeof(GCHeader));
    if (h->next) {
        *slot = (void*)h->next; /* forwarded */
        return;
    }
    if (ch->pinned || ch->demoted) {
        /* Chunk-mate of a conservative pin (stack word / region word hit a
         * DIFFERENT object of this chunk): the chunk will not move, but this
         * object must still be declared a survivor — marked and queued so its
         * own fields join the rewrite closure. Returning bare here left it
         * unmarked: the recycle pass finalized it (dispose_mem freeing a live
         * container's raw buffer) and tombstoned it, while every precise slot
         * kept pointing at the readable zombie. Demoted chunks are old-gen:
         * nothing to do. */
        if (!ch->demoted) gc_pin_young(p);
        return;
    }
    if (h->marked) return; /* pinned this cycle: address is its identity */
    if (_gc_rescue_reporting && _gc_rescue_holder_base) {
        static int rescue_reports = 0;
        if (rescue_reports < 40) {
            rescue_reports++;
            /* Target name: p passed exact_ok validation — a live young body,
             * its metadata pointer is safe to read for the report. Holder
             * name: recorded by gc_old_scan_young_refs for live (non-corpse)
             * holders. */
            const char* tname = NULL;
            if (!h->is_string && h->size >= (int)sizeof(void*)) {
                EmperorClassMetadata* tm = *(EmperorClassMetadata**)p;
                if (tm) tname = tm->name;
            }
            fprintf(stderr,
                    "emperor gc: GC_VERIFY rescue: %s [%s %p] +0x%zx -> young %p%s%s (missed barrier/root)\n",
                    _gc_rescue_holder ? _gc_rescue_holder : "?",
                    _gc_rescue_holder_kind ? _gc_rescue_holder_kind : "?",
                    _gc_rescue_holder_base,
                    (size_t)((char*)slot - _gc_rescue_holder_base), p,
                    tname ? " type=" : "", tname ? tname : "");
        }
    }
    *slot = gc_promote_young(h);
}

/* Pin a young object hit by a conservative word: it must NOT move (the word
 * cannot be rewritten). The chunk is demoted at minor end. A later hit on
 * an already-pinned CHUNK must still queue an unmarked object: the chunk
 * may have been pinned through a DIFFERENT object whose walk never reached
 * this one (chunk-mate chains — B->C->D where pinning B pins the chunk but
 * only B's fields get walked unless C is queued too). */
static void gc_pin_young(void* obj) {
    YoungChunk* ch = gc_chunk_of(obj);
    if (!ch || ch->demoted) return;
    GCHeader* h = (GCHeader*)((char*)obj - sizeof(GCHeader));
    if (h->next) return;      /* promoted earlier this cycle: tombstone */
    if (h->marked) return;    /* already queued (or walked) this cycle */
    h->marked = 1;
    ch->pinned = 1;
    _gc_pin_total++;
    gc_evac_push(obj); /* its precise fields still get rewritten */
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
    /* Generational-only membership mirror (the write barrier's old-target
     * check is its sole reader, and barriers early-out unless
     * _gc_generational). Charging the insert in default/GC_DISABLE builds
     * pure waste: with collections off the set never drains and grows to
     * hold every block ever allocated — measured 10.5% of the whole no-GC
     * compiler self-compile plus gigabytes of RSS. */
    if (_gc_generational) gc_pending_old_add(user);
}

static void* gc_promote_young(GCHeader* h) {
    int asize = (h->size + 7) & ~7;
    if (asize < 8) asize = 8;
    int total = (int)sizeof(GCHeader) + asize;
    char* user = (char*)h + sizeof(GCHeader);
    GCHeader* nb = (GCHeader*)malloc(total);
    if (!nb) {
        /* OOM mid-minor: keep the object at its address — pin it, so the
         * pending rewrite never captures a dangling pointer. */
        h->marked = 1;
        YoungChunk* ch = gc_chunk_of(user);
        if (ch) ch->pinned = 1;
        gc_evac_push(user);
        return user;
    }
    memcpy(nb, h, total);
    nb->next = _emperor_gc_allocation_list;
    nb->marked = 0;
    _emperor_gc_allocation_list = nb;
    _emperor_gc_total_allocated += total;
    gc_pending_append((char*)nb + sizeof(GCHeader));
    _gc_promote_count++;
    /* Forwarding tombstone: marked set (this-cycle survivor) and the idle
     * next field holds the new USER address; size stays intact so chunk
     * walks keep their stride. */
    h->marked = 1;
    h->next = (GCHeader*)((char*)nb + sizeof(GCHeader));
    gc_evac_push((char*)nb + sizeof(GCHeader));
    return (char*)nb + sizeof(GCHeader);
}

/* EVACUATE-mode body walk of one object (an old-generation copy, or a
 * pinned young object still at its address). refmap-described slots are
 * rewritten precisely; mapless bodies (foreign metadata) fall back to a
 * conservative PIN scan — never a rewrite, since which word is the pointer
 * is unknowable. */
static EMPEROR_NO_ASAN void gc_evacuate_body(void* user) {
    GCHeader* h = (GCHeader*)((char*)user - sizeof(GCHeader));
    if (h->is_string || h->size < (int)sizeof(void*)) return;
    EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
    if (meta && meta->refmap) {
        gc_refmap_walk(meta->refmap, 1, (char*)user, (char*)user, h->size,
                       meta ? meta->name : NULL, 0);
        return;
    }
    void** p = (void**)user;
    size_t words = (size_t)h->size / sizeof(void*);
    for (size_t i = 0; i < words; i++) {
        void* owner = gc_young_owner_of(p[i]);
        if (owner) gc_pin_young(owner);
    }
}

static void gc_refmap_abort(const char* what, const int32_t* m, const char* type_name, int32_t idx) {
    fprintf(stderr,
            "emperor gc: CORRUPT REF-MAP (%s) in type %s map=%p node=%d — aborting\n",
            what, type_name ? type_name : "?", (const void*)m, (int)idx);
    abort();
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
                gc_refmap_abort("slot offset outside object", m, type_name, idx);
            }
            if (sub == -1) {
                if (_gc_walk_evacuate) {
                    /* Minor: rewrite a young base in place (forwarding or
                     * promote); everything else is left untouched. */
                    gc_evacuate_slot((void**)(base + off));
                    continue;
                }
                void* candidate = *(void**)(base + off);
                void* owner = NULL;
                /* Interior-tolerant: RDENUM write-chain aliases legitimately
                 * put enum PAYLOAD addresses (owner base+16) into ref slots;
                 * resolve to the containing object (active or demoted chunk)
                 * and follow its forwarding if it already moved. Exact bases
                 * resolve to themselves. */
                YoungChunk* ch = gc_maybe_nursery(candidate) ? gc_chunk_of(candidate) : NULL;
                if (ch) {
                    ch->mark_hit = 1;
                    owner = gc_young_owner_of_any(candidate);
                    if (owner) {
                        GCHeader* yh = (GCHeader*)((char*)owner - sizeof(GCHeader));
                        if (yh->next) owner = (void*)yh->next;
                    }
                }
                if (!owner) {
                    /* maybe_nursery is a RANGE pre-filter only: with several
                     * superchunks, [lo, hi) can span unrelated allocations
                     * (promoted malloc blocks under ASan's allocator, any
                     * mmap'd neighbor). Only a chunk-index hit means "young";
                     * a miss MUST fall back to the sorted index or old-gen
                     * references landing in the span would never be marked. */
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
/* Drain the mark worklist. Every child pushed by gc_refmap_walk's mark path
 * (typed-region elements, frame-chain struct slots) sits on the stack
 * WITHOUT its body being walked — only this loop expands a marked object's
 * fields into marked children. The root phases that push via the ref-map
 * walker must be followed by a drain, or the pushed object survives while
 * its referents are judged dead (the FuncParamTypes.param_types List
 * corruption: dispose_mem zeroed buf while find_func_param_types still
 * walked it). */
static void gc_mark_drain(void) {
    while (_emperor_gc_mark_stack_top > 0) {
        GCHeader* cur = _emperor_gc_mark_stack[--_emperor_gc_mark_stack_top];
        if (cur->is_string) continue;

        char* user = (char*)cur + sizeof(GCHeader);
        EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
        const int32_t* map = meta ? meta->refmap : NULL;
        if (map) {
            /* Precise walk: only the declared pointer locations are read. */
            gc_refmap_walk(map, 1, user, user, cur->size,
                           meta ? meta->name : NULL, 0);
            continue;
        }

        if (_emperor_gc_verify && meta) {
            gc_verify_warn_mapless(meta->name);
        }

        /* No ref-map (foreign metadata, or a freshly allocated block between
         * zeroing and metaptr stamping): conservative scan of the whole
         * object body, as before. The class metadata at offset 0 is a global
         * constant (never GC-tracked, so the is_tracked check naturally
         * skips it). This is necessary because a field can be a struct that
         * *contains* pointers without the field itself being a single
         * pointer — e.g. `Option<T>` lays out as { ptr metadata, i32 tag,
         * ptr payload }, and `ref<T>`/node-link fields are reached through
         * those inner payload pointers. */
        void** ptr = (void**)user;
        size_t word_count = (size_t)cur->size / sizeof(void*);
        for (size_t i = 0; i < word_count; i++) {
            void* candidate = ptr[i];
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
}

static EMPEROR_NO_ASAN void _emperor_gc_mark_object(void* obj) {
    if (!obj || _emperor_gc_mark_failed) return;
    if (gc_maybe_nursery(obj)) {
        YoungChunk* ch = gc_chunk_of(obj);
        if (ch) {
            ch->mark_hit = 1;
            GCHeader* yh = (GCHeader*)((char*)obj - sizeof(GCHeader));
            if (yh->next) {
                _emperor_gc_mark_object((void*)yh->next);
                return;
            }
            /* marked==2: finalized dead in a demoted chunk — a stale
             * conservative word must not resurrect an object whose
             * dispose_mem already ran. */
            if (yh->marked) return;
            yh->marked = 1;
            if (!gc_mark_stack_push(yh)) { _emperor_gc_mark_failed = 1; return; }
            /* FALL THROUGH into the drain loop: a young/demoted object's
             * closure must be walked too — returning here would leave the
             * pushed header unprocessed and its referents unmarked. */
            gc_mark_drain();
            return;
        }
    }
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
static void _emperor_gc_finalize(GCHeader* h) {
    if (h->is_string) return;
    if (h->size < (int)sizeof(void*)) return;
    void* user = (char*)h + sizeof(GCHeader);
    EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
    if (meta && meta->destructor) {
        meta->destructor(user);
    }
}

static size_t _emperor_gc_sweep(void) {
    size_t freed = 0;
    /* Phase A: unlink all dead objects first (no user code runs here). Dead
     * headers are tombstoned (marked == 2) for the index compaction below —
     * the index ARRAY is not touched here: punching NULL holes mid-walk
     * would break its sortedness and misdirect later exact lookups (the
     * lookup binary search needs a strictly ordered array). Survivors get
     * marked reset to 0; 2 is only ever set on already-unlinked headers. */
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
    /* Compact the index: drop the entries of tombstoned headers in one
     * linear pass, BEFORE the frees (headers still allocated and readable).
     * Runs before control returns (single-threaded), i.e. before any new
     * allocation can recycle a just-freed address — so the index never holds
     * a dangling entry for the next refresh's merge to duplicate. */
    {
        size_t w = 0;
        for (size_t r = 0; r < _emperor_gc_sorted_count; r++) {
            GCHeader* h = (GCHeader*)((char*)_emperor_gc_sorted[r] - sizeof(GCHeader));
            if (h->marked != 2) _emperor_gc_sorted[w++] = _emperor_gc_sorted[r];
        }
        _emperor_gc_sorted_count = w;
    }
    /* Phase C: free the dead. */
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

static void gc_scan_regions_minor(void* raw_co_sp) {
    extern int _emperor_gc_on_coroutine;
    for (size_t r = 0; r < _emperor_gc_scan_region_count; r++) {
        GCScanRegion* reg = &_emperor_gc_scan_regions[r];
        if (reg->elem_map) {
            if (!_gc_region_pin_mode) {
                /* Rewrite mode processes typed regions as the LAST root
                 * step of Phase D (after the quarantine pins — see
                 * gc_minor): every pin source must be honored before an
                 * element rewrite can promote a shared object. */
                continue;
            }
            /* REGION_PINS mode — the element PIN pass MUST run before any
             * root evacuation (this is Phase A). These slots are never
             * rewritten (pins-only semantics), so a young element target
             * that some EARLIER-running root promotes would leave a frozen
             * tombstone in the slot — every later read through it resolves
             * the object's stale pre-evacuation children. This ordering
             * used to be violated (the walk sat in Phase D behind the
             * globals/frame-chain evacuations) and only worked because the
             * conservative main-stack cover pinned recently-stored values
             * first; NO_STACK_COVER exposed the bug (container elements
             * reading tombstones — the POSTMINOR "missed rewrite" storm
             * and downstream List.at corruption). */
            _gc_walk_typed = 1;
            _gc_walk_evacuate = 1;
            for (uint64_t i = 0; i < reg->elem_count; i++) {
                gc_refmap_walk(reg->elem_map, 1, reg->base + i * reg->elem_stride,
                               reg->base + i * reg->elem_stride,
                               (int32_t)reg->elem_stride, "typedbuf", 0);
            }
            _gc_walk_evacuate = 0;
            _gc_walk_typed = 0;
            continue;
        }
        /* conservative: pin young hits (words cannot be rewritten) */
        char* lo = reg->live_lo;
        if (lo < reg->base) lo = reg->base;
        /* The RUNNING coroutine's region: live_lo is its PREVIOUS park
         * position — deeper than the current sp. Frames between the current
         * sp and that stale mark are live RIGHT NOW; without this cap the
         * scan misses them and their young references get recycled (same
         * capping the major's region scan has always done). */
        if (_emperor_gc_on_coroutine &&
            (char*)raw_co_sp >= reg->base &&
            (char*)raw_co_sp < reg->base + reg->bytes &&
            (char*)raw_co_sp < lo) {
            lo = (char*)raw_co_sp;
        }
        lo = (char*)((uintptr_t)lo & ~((uintptr_t)sizeof(void*) - 1));
        char** p = (char**)lo;
        char** end = (char**)(reg->base + reg->bytes);
        size_t hits = 0;
        for (; p < end; p++) {
            void* owner = gc_young_owner_of(*p);
            if (owner) { hits++; gc_pin_young(owner); }
        }
    }
}

/* Old-generation -> young reference scan (the barrier fallback body —
 * see _gc_rs_fallback above for when it runs). Under GC_VERIFY it runs
 * AFTER evacuation instead: any promotion it performs then is a missed
 * barrier or missed root, reported loudly (holder name + slot offset via
 * the _gc_rescue_* context). */
static void gc_old_scan_young_refs(void) {
    for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) {
        if (h->is_string || h->size < (int)sizeof(void*)) continue;
        if (_gc_rescue_reporting) {
            /* The metadata word of a scribbled corpse can be arbitrary
             * poison (0xc0ffee00 passes any plausibility check) — never
             * dereference it here; the address alone identifies the holder. */
            _gc_rescue_holder = "old?";
            _gc_rescue_holder_base = (char*)h + sizeof(GCHeader);
            _gc_rescue_holder_kind = "old-malloc";
        }
        gc_evacuate_body((char*)h + sizeof(GCHeader));
    }
    for (YoungChunk* c = _gc_old_pinned; c; c = c->next_active) {
        char* p = c->base;
        while (p < c->top) {
            GCHeader* h = (GCHeader*)p;
            int asize = (h->size + 7) & ~7;
            if (asize < 8) asize = 8;
            /* marked==2: finalized dead — its body is garbage, skip.
             * h->next: promoted-away tombstone — its first body word IS the
             * forwarding address, which gc_evacuate_body would dereference
             * as metadata (the live copy is walked via the malloc list). */
            if (h->marked != 2 && !h->next) {
                if (_gc_rescue_reporting) {
                    /* Live demoted survivor (frozen chunk memory — the meta
                     * word is as valid as when it demoted): name it for the
                     * report; only corpses (marked==2, skipped) can poison. */
                    EmperorClassMetadata* dm =
                        (!h->is_string && h->size >= (int)sizeof(void*))
                            ? *(EmperorClassMetadata**)((char*)h + sizeof(GCHeader))
                            : NULL;
                    _gc_rescue_holder = (dm && dm->name) ? dm->name : "pinned?";
                    _gc_rescue_holder_base = (char*)h + sizeof(GCHeader);
                    _gc_rescue_holder_kind = "old-pinned";
                }
                gc_evacuate_body((char*)h + sizeof(GCHeader));
            }
            p += sizeof(GCHeader) + asize;
        }
    }
    _gc_rescue_holder = NULL;
    _gc_rescue_holder_base = NULL;
    _gc_rescue_holder_kind = NULL;
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

static void gc_card_scan_chunk_object(char* user) {
    GCHeader* h = (GCHeader*)(user - sizeof(GCHeader));
    if (h->next) return;        /* forwarding tombstone: live data is the
                                 * promoted copy (walked via the malloc list).
                                 * Chunk headers never link a list — next is
                                 * idle until promotion forwards it. */
    if (h->marked == 2) return; /* finalized corpse: body is garbage */
    gc_evacuate_body(user);
}

static void gc_card_scan_malloc_block(char* user) {
    /* Malloc blocks: h->next is the ALLOCATION-LIST LINK (no tombstone
     * semantics — they die by free at the major sweep, and cards drain at
     * every minor BEFORE any block can die, so every tabled block is
     * alive here). marked is major-mark state: 0 outside a major. */
    GCHeader* h = (GCHeader*)(user - sizeof(GCHeader));
    if (h->marked == 2) return; /* defensive: never set on malloc blocks */
    gc_evacuate_body(user);
}

/* Objects of DEMOTED chunk c overlapping [lo, hi). */
static void gc_card_scan_chunk(YoungChunk* c, char* lo, char* hi) {
    if (!c->demoted) return; /* active young chunk: its objects join the
                              * promotion closure via the roots, never cards */
    if (c->objs && c->nobjs) {
        /* lower bound: first recorded base >= lo */
        size_t lb = 0, ub = c->nobjs;
        while (lb < ub) {
            size_t mid = lb + (ub - lb) / 2;
            if (c->objs[mid] < lo) lb = mid + 1;
            else ub = mid;
        }
        size_t i = lb;
        if (i > 0) {
            /* predecessor spans into the card? (user + size > lo) */
            char* pb = c->objs[i - 1];
            GCHeader* ph = (GCHeader*)(pb - sizeof(GCHeader));
            if (ph->size > 0 && pb + ph->size > lo) gc_card_scan_chunk_object(pb);
        }
        for (; i < c->nobjs && c->objs[i] < hi; i++) {
            gc_card_scan_chunk_object(c->objs[i]);
        }
        return;
    }
    /* objs[] allocation failed at bump time: stride-walk the chunk and
     * overlap-test every object (rare, bounded by one chunk). */
    char* p = c->base;
    while (p < c->top) {
        GCHeader* h = (GCHeader*)p;
        int asize = (h->size + 7) & ~7;
        if (asize < 8) asize = 8;
        char* user = p + sizeof(GCHeader);
        if (user < hi && user + h->size > lo) gc_card_scan_chunk_object(user);
        p += sizeof(GCHeader) + asize;
    }
}

static int gc_card_key_cmp(const void* a, const void* b) {
    uintptr_t ka = *(const uintptr_t*)a;
    uintptr_t kb = *(const uintptr_t*)b;
    return ka < kb ? -1 : (ka > kb ? 1 : 0);
}

static void gc_cards_scan(void) {
    if (!_gc_cards_used) return;
    /* The scan resolves card ranges through the malloc sorted index; bring
     * it up to date (this also drains the pending-old mirror the barrier
     * consults — promotions THIS cycle append after the drain and refresh
     * again at the next collection). On refresh failure the index is
     * unusable: degrade to the whole-old-generation walk (correct, slow —
     * the same fallback RS_FALLBACK uses). */
    if (!gc_refresh_sorted()) {
        gc_old_scan_young_refs();
        gc_cards_clear();
        return;
    }
    if (_gc_chunk_index_stale) gc_chunk_index_refresh();
    /* Snapshot + sort the card keys: the object walks then run in address
     * order, and per-object dedup (a big block spanning several dirty
     * cards) is a single comparison. The key buffer is cached across
     * minors like every other collector array. */
    static uintptr_t* keys = NULL;
    static size_t keys_cap = 0;
    if (_gc_cards_used > keys_cap) {
        size_t new_cap = _gc_cards_used * 2;
        uintptr_t* grown = (uintptr_t*)realloc(keys, new_cap * sizeof(uintptr_t));
        if (!grown) {
            gc_old_scan_young_refs();
            gc_cards_clear();
            return;
        }
        keys = grown;
        keys_cap = new_cap;
    }
    size_t n = 0;
    for (size_t i = 0; i < _gc_cards_cap; i++) {
        if (_gc_cards[i]) keys[n++] = _gc_cards[i];
    }
    qsort(keys, n, sizeof(uintptr_t), gc_card_key_cmp);
    _gc_st_cards += n;
    char* last_block = NULL; /* dedup: malloc block scanned by the previous card */
    for (size_t k = 0; k < n; k++) {
        char* lo = (char*)keys[k];
        char* hi = lo + GC_CARD_BYTES;
        /* Demoted-chunk holders (chunk index is base-sorted; slices are
         * 64KB so at most the predecessor + the lower bound overlap). */
        if (gc_maybe_nursery(lo) || gc_maybe_nursery(hi - 1)) {
            size_t cl = 0, ch_ = _gc_chunk_index_n;
            while (cl < ch_) {
                size_t mid = cl + (ch_ - cl) / 2;
                if (_gc_chunk_index[mid]->base < lo) cl = mid + 1;
                else ch_ = mid;
            }
            if (cl > 0) {
                YoungChunk* p = _gc_chunk_index[cl - 1];
                if (p->end > lo) gc_card_scan_chunk(p, lo, hi);
            }
            for (size_t ci = cl; ci < _gc_chunk_index_n &&
                                 _gc_chunk_index[ci]->base < hi; ci++) {
                gc_card_scan_chunk(_gc_chunk_index[ci], lo, hi);
            }
        }
        /* Malloc old-gen blocks: the block CONTAINING lo (predecessor of
         * the lower bound, when it spans past lo) plus every block based
         * inside the card. */
        size_t ml = 0, mh = _emperor_gc_sorted_count;
        while (ml < mh) {
            size_t mid = ml + (mh - ml) / 2;
            if ((char*)_emperor_gc_sorted[mid] < lo) ml = mid + 1;
            else mh = mid;
        }
        size_t bi = ml;
        if (bi > 0) {
            char* pb = (char*)_emperor_gc_sorted[bi - 1];
            GCHeader* ph = (GCHeader*)(pb - sizeof(GCHeader));
            if (pb + ph->size > lo && pb != last_block) {
                last_block = pb;
                gc_card_scan_malloc_block(pb);
            }
        }
        for (; bi < _emperor_gc_sorted_count &&
               (char*)_emperor_gc_sorted[bi] < hi; bi++) {
            char* pb = (char*)_emperor_gc_sorted[bi];
            if (pb != last_block) {
                last_block = pb;
                gc_card_scan_malloc_block(pb);
            }
        }
    }
    gc_cards_clear();
}

/* ---- Nursery chunk supply ---- */

static YoungChunk* gc_chunk_acquire(void) {
    if (!_gc_young_free) {
        /* Budget: raising the flag asks the next safepoint poll for a minor;
         * a C-heavy stretch with no polls gets an in-place EMERGENCY minor at
         * 2x budget (conservative cover: C locals pin their targets). */
        if (_gc_young_chunk_bytes + GC_CHUNK_SLICE > _gc_young_budget) {
            _gc_want_minor = 1;
        }
        if (_gc_young_chunk_bytes + GC_CHUNK_SLICE > _gc_young_budget * 2 &&
            !_gc_in_minor && !_emperor_gc_collecting && _emperor_gc_stack_bottom &&
            !_emperor_gc_disabled) {
            gc_minor(1);
            if (_gc_young_free) goto take_free;
        }
        /* Cut a fresh superchunk (1MB) into free chunks. Malloc'd: large
         * blocks are mmap-backed, and the whole nursery stays mapped for the
         * process lifetime — supers are never returned. */
        char* super = (char*)malloc(GC_SUPER_BYTES);
        if (!super) return NULL;
        ((YoungSuper*)super)->next = _gc_supers;
        _gc_supers = (YoungSuper*)super;
        for (int i = GC_CHUNKS_PER_SUPER - 1; i >= 0; i--) {
            YoungChunk* c = (YoungChunk*)(super + i * GC_CHUNK_SLICE);
            memset(c, 0, GC_CHUNK_HDR);
            c->base = (char*)c + GC_CHUNK_HDR;
            c->top = c->base; c->nobjs = 0;
            c->end = (char*)c + GC_CHUNK_SLICE;
            c->next_free = _gc_young_free;
            _gc_young_free = c;
        }
        if ((char*)super < _gc_nursery_lo) _gc_nursery_lo = super;
        if (super + GC_SUPER_BYTES > _gc_nursery_hi) _gc_nursery_hi = super + GC_SUPER_BYTES;
        {
            /* Record the exact range for gc_maybe_nursery's tight filter.
             * Append into the sorted array (supers are few — tens); on
             * growth failure the filter degrades to the loose span only. */
            if (_gc_super_range_count == _gc_super_range_cap) {
                size_t new_cap = _gc_super_range_cap ? _gc_super_range_cap * 2 : 32;
                char** grown = (char**)realloc(_gc_super_ranges, new_cap * 2 * sizeof(char*));
                if (grown) {
                    _gc_super_ranges = grown;
                    _gc_super_range_cap = new_cap;
                }
            }
            if (_gc_super_range_count < _gc_super_range_cap) {
                size_t pos = 0;
                while (pos < _gc_super_range_count &&
                       (char*)_gc_super_ranges[2 * pos] < super) pos++;
                memmove(&_gc_super_ranges[2 * (pos + 1)],
                        &_gc_super_ranges[2 * pos],
                        (_gc_super_range_count - pos) * 2 * sizeof(char*));
                _gc_super_ranges[2 * pos] = super;
                _gc_super_ranges[2 * pos + 1] = super + GC_SUPER_BYTES;
                _gc_super_range_count++;
            }
        }
    }
take_free:
    if (!_gc_young_free) return NULL;
    YoungChunk* c = _gc_young_free;
    _gc_young_free = c->next_free;
    c->next_free = NULL;
    c->next_active = _gc_young_active;
    _gc_young_active = c;
    _gc_young_chunk_bytes += GC_CHUNK_SLICE;
    gc_chunk_index_insert(c);
    _gc_young_cur = c;
    return c;
}

/* Evacuation-mode frame-chain walk (the running stack's head plus every
 * parked coroutine's saved head — mirror of gc_mark_frame_chain). */
static void gc_evacuate_frame_chain(void* head) {
    for (EmperorGcFrame* f = (EmperorGcFrame*)head; f; f = f->prev) {
        for (int32_t i = 0; i < f->slot_count; i++) {
            const EmperorGcSlot* s = &f->slots[i];
            if (s->map == _emperor_gc_pin_refmap) {
                /* Mirror of a parameterized reference: the minor's pin
                 * pre-pass already pinned the target in place (and queued
                 * its fields for rewriting). The slot value itself must
                 * stay the ORIGINAL address — the frame's SSA copies of the
                 * parameter read it verbatim. */
                continue;
            }
            if (s->map == NULL) {
                gc_evacuate_slot((void**)s->addr);
            } else {
                gc_refmap_walk(s->map, 1, (char*)s->addr, (char*)s->addr,
                               0x40000000, "gcframe", 0);
            }
        }
    }
}

/* Pin-marker slot pre-pass (runs BEFORE any promotion — Phase-A order): a
 * pin slot's target must be pinned in place while its frame is live, or the
 * SSA parameter copies sharing the value would dangle into a recycled
 * chunk once evacuation moves the object. Exact bases pin directly;
 * interior words (enum-payload-style leaks) pin their owning object. */
static void gc_pin_frame_chain(void* head) {
    for (EmperorGcFrame* f = (EmperorGcFrame*)head; f; f = f->prev) {
        for (int32_t i = 0; i < f->slot_count; i++) {
            const EmperorGcSlot* s = &f->slots[i];
            if (s->map != _emperor_gc_pin_refmap) continue;
            void* p = *(void**)s->addr;
            if (!p || !gc_maybe_nursery(p)) continue;
            YoungChunk* ch = gc_chunk_of(p);
            if (!ch || ch->demoted) continue; /* old already: cannot move */
            void* owner = gc_owner_in_chunk(p, ch);
            if (owner) gc_pin_young(owner);
        }
    }
}

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

/* Verify helper (minor): walk one survivor's ref-map and report every slot
 * still holding a forwarded tombstone address — the rewrite pass missed it. */
static void gc_report_tombstone_slots(const int32_t* m, int32_t idx, char* base,
                                      int root_size, const char* type_name) {
    int32_t total = m[0];
    if (idx < 1 || idx >= total) return;
    int32_t kind = m[idx];
    if (kind == 1) {
        int32_t n = m[idx + 1];
        for (int32_t i = 0; i < n; i++) {
            int32_t off = m[idx + 2 + 2 * i];
            int32_t sub = m[idx + 3 + 2 * i];
            if (sub == -1) {
                void* v = *(void**)(base + off);
                if (gc_maybe_nursery(v)) {
                    YoungChunk* ch = gc_chunk_of(v);
                    if (ch && !ch->demoted) {
                        GCHeader* h = (GCHeader*)((char*)v - sizeof(GCHeader));
                        if (h->next) {
                            fprintf(stderr,
                                    "emperor gc: GC_VERIFY: UNREWRITTEN slot %s+%d still -> tombstone %p (missed rewrite)\n",
                                    type_name ? type_name : "?", off, v);
                        }
                    }
                }
            } else if (sub != 0) {
                gc_report_tombstone_slots(m, sub, base + off, root_size, type_name);
            }
        }
    } else if (kind == 2) {
        int32_t n = m[idx + 1];
        int64_t tag = *(int64_t*)(base + 8);
        if (tag >= 0 && tag < n) {
            int32_t sub = m[idx + 2 + tag];
            if (sub != 0) gc_report_tombstone_slots(m, sub, base + 16, root_size, type_name);
        }
    }
}

static EMPEROR_NO_ASAN void gc_minor(int conservative_cover) {
    if (_gc_in_minor || !_emperor_gc_stack_bottom) return;
    _gc_in_minor = 1;
    _emperor_gc_collecting = 1;
    _emperor_gc_collect_count++;
    uint64_t _gc_st_t0 = 0; /* stats: exclude the genfull-nested minor */
    if (_gc_stats_on && !_gc_in_genfull) { _gc_st_t0 = _gc_st_ns_now(); _gc_st_minors++; }

    /* Phase A — conservative PIN pass (must precede every promotion: once an
     * object is forwarded, the word pointing at the tombstone could never be
     * honored). Covers the registered conservative regions (coroutine stacks
     * and any legacy-registered container buffers) always, the typed-region
     * element pin walk (see gc_scan_regions_minor), and — only when
     * EMPEROR_GC_STACK_COVER=1 re-enables it — the main stack. */
    jmp_buf _gc_register_buf;
    setjmp(_gc_register_buf);
    __asm__ volatile("" ::: "memory");
    void* raw_co_sp = _emperor_gc_get_stack_pointer();
    void* stack_top = raw_co_sp;
    extern void* _emperor_gc_main_watermark;
    extern int _emperor_gc_on_coroutine;
    if (_emperor_gc_on_coroutine && _emperor_gc_main_watermark) {
        stack_top = _emperor_gc_main_watermark;
    }
    gc_scan_regions_minor(raw_co_sp);
    if (conservative_cover) {
        void** p = (void**)stack_top;
        void** end = (void**)_emperor_gc_stack_bottom;
        for (; p < end; p++) {
            void* owner = gc_young_owner_of(*p);
            if (owner) gc_pin_young(owner);
        }
    }
    /* Pin-marker slots (parameterized-reference mirrors): pin their targets
     * BEFORE any promotion can move them (the mirrors exist precisely
     * because unhomed SSA copies share the value and cannot be rewritten).
     * Covers the running stack's chain plus every parked coroutine's. */
    gc_pin_frame_chain(_emperor_gc_frame_head);
    _emperor_sched_each_frame_head(gc_pin_frame_chain);

    /* Explicit "collect now": drop the quarantine cover (young garbage must
     * die this cycle; the conservative cover above holds the in-flight
     * temps). Auto cycles keep the ring and promote it below. */
    if (_emperor_gc_explicit_cycle) {
        _emperor_gc_quarantine_count = 0;
        _emperor_gc_quarantine_next = 0;
    }

    _gc_walk_evacuate = 1;

    /* Phase B — card-table fallback: walks the WHOLE old generation's
     * ref-maps before the roots to discover every old->young edge. Off by
     * default since the barriers are emitted (see _gc_rs_fallback); the
     * GC_VERIFY pass below keeps a post-evacuation run as the detector. */
    if (_gc_rs_fallback) {
        gc_old_scan_young_refs();
    }

    /* Phase C — dirty cards: the barrier-marked old-generation pages. This
     * is the minor's ONLY old-gen walk in the default configuration; it
     * evacuates every old->young edge on (or spanning into) a dirty card
     * and clears the table. */
    gc_cards_scan();

    /* Phase D — precise roots, evacuated in place. */
    for (int i = 0; i < _emperor_gc_global_root_count; i++) {
        gc_evacuate_slot(_emperor_gc_global_roots[i]);
    }
    for (size_t i = 0; i < _emperor_gc_pinned_count; i++) {
        gc_evacuate_slot(&_emperor_gc_pinned[i]);
    }
    /* Frame chains: bare slots rewrite; struct slots walk their map. */
    gc_evacuate_frame_chain(_emperor_gc_frame_head);
    /* Parked coroutine chains: the scheduler saved each stack's head. */
    _emperor_sched_each_frame_head(gc_evacuate_frame_chain);
    /* Typed regions: the element pass runs as the last root step below
     * (rewrite mode) or in Phase A (REGION_PINS bisect). */
    /* Quarantine ring (auto cycles): the most recent allocations are still
     * in SSA/C flight — nested-new constructor calls, in-flight string work.
     * PIN them in place (never promote): their live copies exist only as
     * raw SSA/spill words no descriptor slot can rewrite, so moving the
     * object would leave those copies reading a recycled chunk (the exact
     * exposure the ring exists to cover — evacuation only looked safe
     * because the conservative main-stack cover re-pinned everything).
     * Bump allocation keeps the 512 ring entries clustered in the most
     * recent 1-2 chunks, so the extra demotion per minor is bounded. */
    if (!_emperor_gc_explicit_cycle) {
        for (size_t i = 0; i < _emperor_gc_quarantine_count; i++) {
            void* p = _emperor_gc_quarantine[i];
            if (!p || !gc_maybe_nursery(p)) continue;
            YoungChunk* qch = gc_chunk_of(p);
            if (!qch || qch->demoted) continue; /* old already: cannot move */
            void* owner = gc_owner_in_chunk(p, qch);
            if (owner) gc_pin_young(owner);
        }
    }

    /* Typed-region element pass — the LAST root step (rewrite mode; the
     * REGION_PINS bisect runs it up in Phase A instead). Running it after
     * the quarantine pins keeps every pin-only source (frame mirrors,
     * quarantine ring) honored BEFORE an element rewrite can promote an
     * object they share: a pinned chunk's elements take the pinned-survivor
     * path (no move, no rewrite), everything else promotes and the element
     * slot follows. */
    if (!_gc_region_pin_mode) {
        for (size_t r = 0; r < _emperor_gc_scan_region_count; r++) {
            GCScanRegion* reg = &_emperor_gc_scan_regions[r];
            if (!reg->elem_map) continue;
            _gc_walk_typed = 1;
            for (uint64_t i = 0; i < reg->elem_count; i++) {
                gc_refmap_walk(reg->elem_map, 1, reg->base + i * reg->elem_stride,
                               reg->base + i * reg->elem_stride,
                               (int32_t)reg->elem_stride, "typedbuf", 0);
            }
            _gc_walk_typed = 0;
        }
    }

    /* Phase E — drain the promotion worklist (transitive closure: each
     * promoted copy's / pinned object's fields get rewritten in turn). */
    while (_gc_evac_stack_top > 0) {
        void* u = _gc_evac_stack[--_gc_evac_stack_top];
        gc_evacuate_body(u);
    }
    if (_gc_evac_overflow) {
        /* Worklist memory exhausted mid-closure: partial rewriting means
         * chunks may NOT be recycled (a slot may still point anywhere).
         * Degrade: pin every active chunk (whole nursery retained). */
        _gc_evac_overflow = 0;
        _gc_evac_stack_top = 0;
        for (YoungChunk* c = _gc_young_active; c; c = c->next_active) {
            c->pinned = 1;
            char* p = c->base;
            while (p < c->top) {
                GCHeader* h = (GCHeader*)p;
                if (!h->marked && !h->next) h->marked = 1; /* keep everything */
                int asize = (h->size + 7) & ~7;
                if (asize < 8) asize = 8;
                p += sizeof(GCHeader) + asize;
            }
        }
    }

    /* GC_VERIFY (post-evacuation, pre-recycle): a PRECISE slot still
     * pointing at a forwarding tombstone is an un-rewritten reference —
     * the promotion closure missed it (frame-slot/map gap). Conservative
     * region words are exempt (they legitimately cannot be rewritten and
     * their targets are pinned, never tombstones). Report the holder. */
    if (_emperor_gc_verify) {
        for (YoungChunk* c = _gc_young_active; c; c = c->next_active) {
            char* p = c->base;
            while (p < c->top) {
                GCHeader* h = (GCHeader*)p;
                int asz = (h->size + 7) & ~7;
                if (asz < 8) asz = 8;
                if (h->next && !h->is_string && h->size >= (int)sizeof(void*)) {
                    /* a tombstone: any PRECISE object whose walk visits a slot
                     * equal to this address was missed — scan precise roots
                     * cheaply by checking surviving objects' ref-map slots. */
                }
                if (!h->next && h->marked && !h->is_string && h->size >= (int)sizeof(void*)) {
                    char* user = p + sizeof(GCHeader);
                    EmperorClassMetadata* m = *(EmperorClassMetadata**)user;
                    if (m && m->refmap) {
                        gc_report_tombstone_slots(m->refmap, 1, user, h->size,
                                                  m ? m->name : NULL);
                    }
                }
                p += sizeof(GCHeader) + asz;
            }
        }
    }

    /* GC_VERIFY (post-evacuation): any old->young edge left is a missed
     * barrier or missed root — this pass rescues it (promotion) and says so.
     * Run before the chunks go away so the rescue is possible at all. */
    if (_emperor_gc_verify) {
        size_t before = _gc_promote_count;
        _gc_rescue_reporting = 1;
        gc_old_scan_young_refs();
        _gc_rescue_reporting = 0;
        if (_gc_promote_count != before) {
            fprintf(stderr,
                    "emperor gc: GC_VERIFY: minor rescued %zu missed old->young edge(s) (missing barrier/root?)\n",
                    _gc_promote_count - before);
        }
    }

    /* Phase F — finalizers for dead young objects (containers releasing
     * malloc'd buffers). They run while every object is still readable and
     * before any chunk is recycled. Allocations inside a finalizer take the
     * old-gen malloc path (in_minor) — judged next cycle. */
    for (YoungChunk* c = _gc_young_active; c; c = c->next_active) {
        char* p = c->base;
        while (p < c->top) {
            GCHeader* h = (GCHeader*)p;
            if (!h->marked && !h->next) {
                _emperor_gc_finalize(h);
            }
            int asize = (h->size + 7) & ~7;
            if (asize < 8) asize = 8;
            p += sizeof(GCHeader) + asize;
        }
    }

    /* Phase G — recycle: pinned chunks demote to old-gen retention (their
     * survivors join the sorted index so the next major and every resolve
     * can find them; their marked resets to 0 for that major), the rest go
     * back to the free list. Demoted chunks are PAGE-RETAINED (space is
     * never freed — the pinned objects cannot move) but their dead objects
     * are still judged by every major (finalized once, tombstoned, and the
     * young-bytes charge drops): conservative retention lasts exactly as
     * long as the conservative WORD stays visible, matching the non-moving
     * collector's semantics. */
    YoungChunk** plink = &_gc_young_active;
    /* Recharge from scratch: PREVIOUSLY demoted survivors (marked==0 after
     * their major reset, or still-set from this cycle — anything not a
     * promoted tombstone and not finalized-dead) PLUS this cycle's pins.
     * Resetting to zero alone would erase the charge of survivors that
     * live in long-demoted chunks. */
    _gc_young_bytes = 0;
    for (YoungChunk* c = _gc_old_pinned; c; c = c->next_active) {
        char* q = c->base;
        while (q < c->top) {
            GCHeader* hh = (GCHeader*)q;
            int asz = (hh->size + 7) & ~7;
            if (asz < 8) asz = 8;
            if (!hh->next && hh->marked != 2) {
                _gc_young_bytes += sizeof(GCHeader) + asz;
            }
            q += sizeof(GCHeader) + asz;
        }
    }
    while (*plink) {
        YoungChunk* c = *plink;
        if (c->pinned) {
            *plink = c->next_active;
            c->next_active = _gc_old_pinned;
            c->demoted = 1;
            _gc_old_pinned = c;
            _gc_young_chunk_bytes -= GC_CHUNK_SLICE;
            char* p = c->base;
            while (p < c->top) {
                GCHeader* h = (GCHeader*)p;
                int asize = (h->size + 7) & ~7;
                if (asize < 8) asize = 8;
                if (h->next) {
                    /* promoted-away tombstone: charged nothing, stays put */
                } else if (h->marked) {
                    /* DO NOT gc_pending_append: the sorted index is the
                     * MALLOC old-gen block table only. Chunk objects resolve
                     * through _gc_chunk_index (gc_chunk_of) — appending them
                     * here seeded the index with ghost entries that outlive
                     * chunk reclamation, never compact away (the sweep walks
                     * the malloc list, not chunks), accumulate per minor,
                     * and then shadow real malloc blocks in gc_resolve_block:
                     * a mark resolves a live object's pointer to a ghost
                     * base, the real object goes unmarked and is swept while
                     * still referenced (the List<Token> corruption). */
                    h->marked = 0;
                    _gc_young_bytes += sizeof(GCHeader) + asize;
                    /* Survivor-by-demotion field closure: an object that
                     * survives ONLY because its CHUNK pinned (a chunk-mate
                     * of the actually-hit object) never got an
                     * evacuate_slot visit — nothing queued ITS fields for
                     * rewriting. Its young referents then promoted via
                     * other roots and this field kept an un-rewritable
                     * tombstone (the POSTMINOR demotedbody storm; benign
                     * only while every container-held value stayed pinned
                     * forever under pins-only regions). Queue every
                     * demoting survivor; the post-Phase-G drain below
                     * rewrites their fields (promoting still-young
                     * referents) once the chunk list is settled. */
                    gc_evac_push(p + sizeof(GCHeader));
                } else {
                    /* dead this minor: phase F already finalized it —
                     * tombstone so the major's logical sweep skips it
                     * (it was never charged, decrementing would underflow) */
                    h->marked = 2;
                }
                p += sizeof(GCHeader) + asize;
            }
        } else {
            plink = &c->next_active;
        }
    }
    /* Demotion-driven major scheduling: malloc-path bytes rarely cross the
     * old-gen threshold in a young-churn workload, so without this the
     * old-pinned list (and the chunk index) grows without bound — every
     * minor's conservative cover pins a few more chunks, demoted chunks
     * never leave, and the index qsort spirals. Ask the next poll for a
     * full major (which reclaims all-dead chunks) once the demoted chunk
     * count passes a floor AND has doubled since the last major — with
     * genuinely-live pinned data the majors stay geometric, not steady. */
    {
        size_t demoted = 0;
        for (YoungChunk* c = _gc_old_pinned; c; c = c->next_active) demoted++;
        if (demoted >= 64 && demoted >= 2 * _gc_old_pinned_at_major) {
            _emperor_gc_want_collect = 1;
        }
    }

    /* Late-field drain (see the demotion branch above): rewrite the demoting
     * survivors' fields — and anything the GC_VERIFY rescue pass queued after
     * Phase E. MUST run BEFORE the active-chunk recycle loop below: these
     * bodies' fields may point at still-young targets reachable ONLY through
     * them; once the target's chunk is recycled (top reset, index removed)
     * the rewrite can neither promote nor even resolve the target, and the
     * field keeps pointing at memory fresh allocations reuse. Targets in
     * still-active chunks promote here (and mark nothing else live);
     * targets in demoted chunks are stable either way. Overflow would leave
     * such targets in to-be-recycled chunks — corrupting, not merely stale —
     * so degrade by pinning every active chunk (they demote instead of
     * recycling this cycle). */
    while (_gc_evac_stack_top > 0) {
        void* u = _gc_evac_stack[--_gc_evac_stack_top];
        gc_evacuate_body(u);
    }
    if (_gc_evac_overflow) {
        _gc_evac_overflow = 0;
        _gc_evac_stack_top = 0;
        for (YoungChunk* c = _gc_young_active; c; c = c->next_active) {
            c->pinned = 1;
            char* p = c->base;
            while (p < c->top) {
                GCHeader* h = (GCHeader*)p;
                if (!h->marked && !h->next) h->marked = 1; /* keep everything */
                int asize = (h->size + 7) & ~7;
                if (asize < 8) asize = 8;
                p += sizeof(GCHeader) + asize;
            }
        }
    }

    plink = &_gc_young_active;
    while (*plink) {
        YoungChunk* c = *plink;
        *plink = c->next_active;
        c->next_active = NULL;
        c->top = c->base; c->nobjs = 0;
        c->next_free = _gc_young_free;
        _gc_young_free = c;
        _gc_young_chunk_bytes -= GC_CHUNK_SLICE;
        gc_chunk_index_remove(c);
    }
    _gc_young_cur = NULL;

    _gc_walk_evacuate = 0;

    _gc_in_minor = 0;
    _emperor_gc_collecting = 0;
    _gc_want_minor = 0;
    if (_gc_st_t0) { _gc_st_ns_minor += _gc_st_ns_now() - _gc_st_t0; _gc_st_note_live(); }
}

/* ---- Generational full collection: minor + old-gen mark/sweep ---- */

static void gc_collect_generational(void) {
    uint64_t _gc_st_t0 = 0;
    if (_gc_stats_on) { _gc_st_t0 = _gc_st_ns_now(); _gc_st_genfulls++; _gc_in_genfull = 1; }
    /* Minor first: the nursery is emptied (survivors promoted, pins demoted)
     * so the major marks a single generation with the existing machine.
     * The minor's conservative main-stack cover rides _gc_stack_cover_poll
     * (off by default; EMPEROR_GC_STACK_COVER=1 for the bisect path) — the
     * same precise-root coverage argument as a poll minor applies here. */
    gc_minor(_gc_stack_cover_poll);

    /* Re-entrancy latch for the rest of the cycle. The minor above holds
     * _emperor_gc_collecting only for its own duration; from here on the
     * major runs finalizers — PENGUIN dispose_mem code that polls at every
     * call site. Without the latch a dispose poll starts a NESTED full
     * collection over half-processed state: this pass has already reset
     * earlier chunks' survivors to marked==0, the nested mark judges them
     * against its own roots and the nested pass finalizes LIVE objects
     * in place (the 3c List<Token> corruption: a mid-parse token list's
     * buf freed and zeroed while the parser still walked it). The poll
     * and gc_collect_main already test the flag; holding it for the whole
     * cycle makes "no collection inside a collection" true for the
     * generational path, like it always was for the sequential one. */
    _emperor_gc_collecting = 1;

    jmp_buf _gc_register_buf;
    setjmp(_gc_register_buf);
    __asm__ volatile("" ::: "memory");
    void* raw_co_sp = _emperor_gc_get_stack_pointer();

    if (!gc_refresh_sorted()) {
        _emperor_gc_collecting = 0;
        if (_gc_st_t0) { _gc_st_ns_genfull += _gc_st_ns_now() - _gc_st_t0; _gc_in_genfull = 0; }
        return; /* retain everything this cycle (see gc_refresh_sorted) */
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
    /* Struct-map frame slots and typed-region elements push their children
     * via gc_refmap_walk WITHOUT walking them — drain here, or anything
     * reachable ONLY through those children is judged dead and finalized
     * under live readers (the FuncParamTypes.param_types corruption). The
     * conservative cover below drains opportunistically (each new
     * mark_object drains), but a cover that hits only already-marked words
     * returns early and leaves the stack undrained. */
    gc_mark_drain();

    /* Conservative main-stack cover (EXPERIMENT, 3b bug hunt): the frame
     * descriptors only enumerate emitter-known slots — SSA spill slots and
     * C-frame temporaries holding live refs are invisible to the precise
     * net. Old objects referenced ONLY from there would be swept (and their
     * finalizers run — a live List's buffer freed mid-parse). Mirror the
     * sequential path's explicit-cycle cover here. */
    {
        extern void* _emperor_gc_main_watermark;
        extern int _emperor_gc_on_coroutine;
        void* cover_top = raw_co_sp;
        if (_emperor_gc_on_coroutine && _emperor_gc_main_watermark) {
            cover_top = _emperor_gc_main_watermark;
        }
        _emperor_gc_mark_conservative(_emperor_gc_stack_bottom, cover_top);
    }

    if (_emperor_gc_mark_failed) {
        _emperor_gc_mark_failed = 0;
        _emperor_gc_mark_stack_top = 0;
        for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) h->marked = 0;
        _emperor_gc_collecting = 0;
        return;
    }

    size_t freed;
    if (_gc_stats_on) {
        uint64_t sw0 = _gc_st_ns_now();
        freed = _emperor_gc_sweep();
        _gc_st_ns_sweep += _gc_st_ns_now() - sw0;
    } else {
        freed = _emperor_gc_sweep();
    }
    _emperor_gc_total_allocated -= freed;

    /* Old-pinned chunks: page-retained, but logically swept. Survivors
     * (marked) reset; dead-but-not-yet-finalized objects run their
     * finalizers and tombstone at marked==2 (their young-bytes charge
     * drops). A stale conservative word cannot resurrect a finalized
     * object (mark_object checks). dispose_mem idempotence covers a
     * container touching an already-finalized inner object. A chunk whose
     * every object is dead (this major's mark — including the conservative
     * main-stack cover — reached nothing in it) has NO live word pointing
     * into it: it is returned to the free list, bounding conservative
     * retention to chunks that still hold live pinned survivors (without
     * this, the always-on minor cover demotes a few chunks every cycle and
     * old_pinned grows without bound — the chunk index qsort and the nursery
     * budget then spiral). */
    YoungChunk** plink_old = &_gc_old_pinned;
    while (plink_old && *plink_old) {
        YoungChunk* c = *plink_old;
        char* p = c->base;
        /* mark_hit: a root/conservative word resolved into this chunk during
         * the mark above — quite possibly a TOMBSTONE (the mark then
         * forwarded to the promoted malloc copy, marking nothing here).
         * That reader still reads this chunk's memory, so the chunk is not
         * all-dead even with zero marked objects. */
        int any_live = c->mark_hit;
        c->mark_hit = 0;
        while (p < c->top) {
            GCHeader* h = (GCHeader*)p;
            int asize = (h->size + 7) & ~7;
            if (asize < 8) asize = 8;
            if (!h->next) {
                if (h->marked == 1) {
                    h->marked = 0; /* survivor: re-judged next major */
                    any_live = 1;
                } else if (h->marked == 0) {
                    /* Dead BEFORE the finalizer runs: dispose_mem is penguin
                     * code — while it executes, the object must already read
                     * as finalized-dead (marked==2), never as an unmarked
                     * block another pass could re-finalize or sweep away
                     * under the running disposer. */
                    h->marked = 2;
                    _emperor_gc_finalize(h);
                    _gc_young_bytes -= sizeof(GCHeader) + asize;
                }
                /* marked==2: already finalized dead — stays dead */
            }
            p += sizeof(GCHeader) + asize;
        }
        if (!any_live) {
            *plink_old = c->next_active;
            c->next_active = NULL;
            c->pinned = 0;
            c->demoted = 0;
            c->mark_hit = 0;
            c->top = c->base; c->nobjs = 0;
            c->next_free = _gc_young_free;
            _gc_young_free = c;
            gc_chunk_index_remove(c);
        } else {
            plink_old = &c->next_active;
        }
    }

    size_t live = _emperor_gc_total_allocated;
    size_t live_target = live * 2;
    if (live_target > _emperor_gc_threshold) {
        _emperor_gc_threshold = live_target;
    } else if (freed < live / 4) {
        if (_emperor_gc_threshold < (1ULL << 42)) {
            _emperor_gc_threshold *= 4;
        }
    }
    _emperor_gc_want_collect = 0;
    /* Demotion-major bookkeeping (see the minor-end scheduler): the next
     * demotion trigger compares against the POST-reclaim chunk count. */
    _gc_old_pinned_at_major = 0;
    for (YoungChunk* c = _gc_old_pinned; c; c = c->next_active) {
        _gc_old_pinned_at_major++;
    }
    /* The card table cannot outlive a collection: objects only die inside
     * collections, but this major just freed old-gen holders whose dirty
     * cards are still tabled — the next minor's Phase C would walk through
     * freed (possibly tcache-reused) memory. Cards re-mark via the barriers
     * between collections, so clearing here is lossless. (The major's own
     * leading minor already drained the table; this covers the degenerate
     * paths that reach the major without one.) */
    gc_cards_clear();
    _emperor_gc_collecting = 0;
    if (_gc_st_t0) { _gc_st_ns_genfull += _gc_st_ns_now() - _gc_st_t0; _gc_in_genfull = 0; _gc_st_note_live(); }
}

static EMPEROR_NO_ASAN void gc_collect_main(void) {
    if (_emperor_gc_disabled) return;
    if (!_emperor_gc_stack_bottom || _emperor_gc_collecting) return;
    /* Generational (precise + nursery): a full collect = minor (empty the
     * nursery, conservative cover on explicit cycles) + the old-generation
     * mark/sweep above. Cheap minis (budget flag) stop at gc_minor. */
    if (_gc_generational) {
        if (_emperor_gc_explicit_cycle || _emperor_gc_want_collect) {
            gc_collect_generational();
        } else {
            gc_minor(_gc_stack_cover_poll);
        }
        return;
    }
    _emperor_gc_collecting = 1;
    _emperor_gc_collect_count++;
    uint64_t _gc_st_t0 = 0;
    if (_gc_stats_on) { _gc_st_t0 = _gc_st_ns_now(); _gc_st_fulls++; }

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

    /* Quarantine (precise mode): retain the most recent allocations — those
     * blocks may still be only in SSA/C flight (nested new-expressions,
     * in-flight C string work). An EXPLICIT collect drops the cover
     * instead: "collect now" means young garbage must be judged, and the
     * conservative stack scan this cycle covers the in-flight temps the
     * quarantine would have. The mark-failed path keeps the ring contents:
     * no sweep ran, so the in-flight cover must carry over. */
    if (_emperor_gc_mode_precise) {
        if (_emperor_gc_explicit_cycle) {
            _emperor_gc_quarantine_count = 0;
            _emperor_gc_quarantine_next = 0;
        } else if (_emperor_gc_mark_failed) {
            /* keep the ring — see comment */
        } else {
            for (size_t i = 0; i < _emperor_gc_quarantine_count; i++) {
                _emperor_gc_mark_object(_emperor_gc_quarantine[i]);
            }
            /* The cover is consumed: these blocks were either reachable
             * from the roots anyway or survived via the quarantine — the
             * next cycle judges them by reachability alone. */
            _emperor_gc_quarantine_count = 0;
            _emperor_gc_quarantine_next = 0;
        }
    }

    /* GC_VERIFY differential (legacy mode): everything marked so far (globals +
     * frame chains + regions — the phase-3 precise root set) is restamped
     * 3; the conservative stack scan below then marks the remaining
     * reachable objects 1. Objects left at 1 are what the precise net would
     * MISS — the number that must reach "only explainable strays" before
     * phase 3 drops the conservative scan. Reported once per type name.
     * Precise mode skips the scan entirely (and the differential). */
    int verify_phase = _emperor_gc_verify && !_emperor_gc_mode_precise;
    if (verify_phase) {
        for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) {
            if (h->marked == 1) h->marked = 3;
        }
    }

    if (!_emperor_gc_mode_precise || _emperor_gc_explicit_cycle) {
        _emperor_gc_mark_conservative(_emperor_gc_stack_bottom, stack_top);
    }

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

    size_t freed;
    if (_gc_stats_on) {
        uint64_t sw0 = _gc_st_ns_now();
        freed = _emperor_gc_sweep();
        _gc_st_ns_sweep += _gc_st_ns_now() - sw0;
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
        if (_gc_stats_on) atexit(_gc_st_report);
    }
    const char* kill = getenv("EMPEROR_GC_DISABLE");
    _emperor_gc_disabled = (kill != NULL && kill[0] != '\0' && kill[0] != '0');
    if (_emperor_gc_disabled) {
        _emperor_gc_threshold = 8ULL << 40;
    }
    const char* verify = getenv("GC_VERIFY");
    _emperor_gc_verify = (verify != NULL && verify[0] != '\0' && verify[0] != '0');
    const char* mode = getenv("EMPEROR_GC_MODE");
    _emperor_gc_mode_conservative =
        (mode != NULL && mode[0] != '\0' && strcmp(mode, "conservative") == 0);
    /* Precise is the default (see the mode comment above); "default"/"legacy"
     * explicitly opts back into the old non-generational behavior. */
    _emperor_gc_mode_precise =
        (mode == NULL || mode[0] == '\0' || strcmp(mode, "precise") == 0);
    if (mode != NULL && (strcmp(mode, "default") == 0 ||
                         strcmp(mode, "legacy") == 0)) {
        _emperor_gc_mode_precise = 0;
    }
    /* Generational nursery: precise-only, unless EMPEROR_GC_NOGEN pins the
     * experiment to the non-moving precise behavior (bug bisection). */
    const char* nogen = getenv("EMPEROR_GC_NOGEN");
    _gc_generational = _emperor_gc_mode_precise &&
                       !(nogen != NULL && nogen[0] != '\0' && nogen[0] != '0');
    const char* young = getenv("EMPEROR_GC_YOUNG");
    if (young != NULL && young[0] != '\0') {
        unsigned long long b = strtoull(young, NULL, 10);
        if (b >= GC_CHUNK_SLICE) _gc_young_budget = (size_t)b;
    }
    const char* nofb = getenv("EMPEROR_GC_NO_RS_FALLBACK");
    if (nofb != NULL && nofb[0] != '\0' && nofb[0] != '0') {
        _gc_rs_fallback = 0;
    }
    const char* yesfb = getenv("EMPEROR_GC_RS_FALLBACK");
    if (yesfb != NULL && yesfb[0] != '\0' && yesfb[0] != '0') {
        _gc_rs_fallback = 1;
    }
    const char* nocover = getenv("EMPEROR_GC_NO_STACK_COVER");
    if (nocover != NULL && nocover[0] != '\0' && nocover[0] != '0') {
        _gc_stack_cover_poll = 0;
    }
    const char* yescover = getenv("EMPEROR_GC_STACK_COVER");
    if (yescover != NULL && yescover[0] != '\0' && yescover[0] != '0') {
        _gc_stack_cover_poll = 1;
    }
    const char* regpins = getenv("EMPEROR_GC_REGION_PINS");
    if (regpins != NULL && regpins[0] != '\0' && regpins[0] != '0') {
        _gc_region_pin_mode = 1;
    }
    const char* stress = getenv("EMPEROR_GC_STRESS_EVERY");
    if (stress != NULL && stress[0] != '\0') {
        _emperor_gc_stress_every = (unsigned)strtoul(stress, NULL, 10);
    }
    const char* stress_max = getenv("EMPEROR_GC_STRESS_MAX");
    if (stress_max != NULL && stress_max[0] != '\0') {
        _emperor_gc_stress_max = (unsigned)strtoul(stress_max, NULL, 10);
    }
}

/* ---- Runtime ABI version ----
 * The emitted code and the C runtime evolve in lockstep (ref-maps, write
 * barriers, typed buffer tracking). Consumers that mix an emission against
 * a foreign runtime — a stale .penguin-lib, a JIT module from another
 * build — must compare this symbol and refuse loudly instead of
 * misbehaving. Bump on every layout/protocol change below. */
const char* const _emperor_runtime_abi = "emperor-rt-gc2-3d";

/* ---- GC Info ---- */

uint64_t _emperor_gc_info(void) {
    return (uint64_t)_emperor_gc_total_allocated +
           (_gc_generational ? _gc_young_bytes : 0);
}

int _emperor_gc_probe_tracked(void* user) {
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
    *young_bytes = (uint64_t)(_gc_generational ? _gc_young_bytes : 0);
}

/* ---- Generational write barriers (see emperor_gc.h) ---- */

static int gc_barrier_old_target(void* obj) {
    /* Fast path: nursery writers (the churn common case) — two compares.
     * A range hit whose chunk lookup misses is a FALSE positive (a promoted
     * malloc block under an allocator whose regions interleave with the
     * superchunks) — fall through to the index resolve, never conclude.
     * NOT stale-ok: a stale index misses newly-acquired/demoted chunks —
     * the writer would be judged non-old and the barrier skipped entirely
     * (missed old->young edge, young field target judged dead). The
     * refresh is cheap now (insertion sort over a nearly-sorted array). */
    if (gc_maybe_nursery(obj)) {
        YoungChunk* ch = gc_chunk_of(obj);
        if (ch) return ch->demoted; /* active young: no; demoted: old */
    }
    /* Promoted/malloc'd since the last index refresh: in pending only, so
     * gc_resolve_block would miss them (this check runs BETWEEN collections,
     * unlike every other resolve consumer). */
    if (gc_pending_old_contains(obj)) return 1;
    /* Malloc old-gen block: resolve must hit the exact base; anything else
     * (a stack alloca receiving an inline-struct store, a raw buffer) is not
     * a barrier target — the frame chain / region walk covers it. */
    return gc_resolve_block(obj) == obj;
}

void _emperor_gc_write_barrier(void* obj, void** slot) {
    if (!_gc_generational || _gc_in_minor) return;
    if (!gc_barrier_old_target(obj)) return;
    /* Card semantics: mark the slot's 512B card, no value check. The minor
     * judges each field's CURRENT value while scanning the card, so a
     * store whose value is old now still covers a young value written
     * into the same page later (one card entry per page per minor window,
     * instead of one exact-slot record per store). */
    gc_card_mark(slot);
}

void _emperor_gc_write_barrier_map(void* obj, void* slot, const int32_t* map) {
    /* map is no longer RECORDED (kept for ABI — emissions pass it): the
     * card scan walks the OWNER object's own ref-map, whose layout
     * includes the embedded struct's slots as sub-nodes. map == NULL (a
     * field type whose ref-map failed to compute) still means "no embedded
     * references" — nothing to dirty. */
    if (!_gc_generational || _gc_in_minor || !map) return;
    if (!gc_barrier_old_target(obj)) return;
    gc_card_mark(slot);
}

/* ---- GC-tracked Allocation ---- */

void* _emperor_gc_alloc(int size, int is_string) {
    /* Generational: small objects bump-allocate in the nursery (no malloc,
     * no list insert, no index append — and no inline collection: C-side
     * allocations never collect, the budget flag asks the next safepoint
     * poll for a minor). Everything else (big objects, or any allocation
     * while a minor promotes — promote itself allocates through here) takes
     * the old-gen path below. */
    if (_gc_generational && !_gc_in_minor && size < GC_YOUNG_MAX) {
        return gc_alloc_young(size, is_string);
    }
    int total = (int)sizeof(GCHeader) + size;
    if (_gc_stats_on) { _gc_st_allocs++; _gc_st_alloc_bytes += (size_t)total; }
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

    /* Quarantine (precise mode): record the block in the recent-allocation
     * ring so the next collect retains it even while it is only in SSA/C
     * flight (see the fresh-object quarantine note above). The ring
     * overwrites the oldest entry once full — blocks older than the window
     * are from completed statements and are judged normally. Gate at the
     * call site: the callee's early-out still costs a call per allocation
     * on every non-precise build (the no-GC profile showed 0.5% for the
     * no-op alone). */
    if (_emperor_gc_mode_precise) {
        gc_quarantine_push((char*)header + sizeof(GCHeader));
    }

    /* Poll-mode collection scheduling: crossing the
     * threshold only RAISES the flag; the __gc_poll calls the emitter
     * places at every penguin call/alloc site drain it. Collecting at a
     * penguin safepoint (instead of mid-C here) is what will let the
     * precise frame chains become the authoritative root set in phase 3 —
     * for now the poll collection is the same conservative collect, so the
     * timing shift is the only semantic change. Emergency: a C-heavy
     * stretch with no poll in sight grows past 8x the threshold — collect
     * right here (conservative scan covers C locals), as the legacy path
     * did. Conservative mode (EMPEROR_GC_MODE=conservative) keeps the
     * legacy inline-collect behavior exactly. Generational keeps the
     * malloc path flag-only (its young objects never collect inline, and
     * big-object storms are not a churn pattern worth an emergency full).
     *
     * An inline collection here may run BEFORE this pointer reaches the
     * caller, so the fresh block is marked around it (a marked block skips
     * the body scan — safe, the body is still zero-filled) — otherwise the
     * collection its own allocation triggered would sweep it (dangling
     * return → tcache reuse corrupts the header/list). */
    if (!_emperor_gc_disabled && !_emperor_gc_collecting &&
        _emperor_gc_total_allocated >= _emperor_gc_threshold) {
        int collect_here = !_gc_generational &&
                           (_emperor_gc_mode_conservative ||
                            _emperor_gc_total_allocated >= _emperor_gc_threshold * 8);
        if (collect_here) {
            header->marked = 1;
            gc_collect_auto();
            header->marked = 0;
        } else {
            _emperor_gc_want_collect = 1;
        }
    }

    return (char*)header + sizeof(GCHeader);
}

/* Safepoint poll: called by emitted code before every call and at every
 * allocation site. Cheap when no collection is pending: one load + ret.
 * Generational: the young-budget flag drains with a cheap MINOR only; the
 * old-gen threshold flag (and every stress collect) runs the full
 * minor + old-gen mark/sweep. */
void _emperor_gc_poll(void) {
    if (_gc_stats_on) _gc_st_polls++;
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
    if (!_emperor_gc_want_collect && !_gc_want_minor) return;
    /* Do NOT pre-clear the want flags: gc_collect_main re-checks
     * _emperor_gc_want_collect to pick the generational FULL path over a
     * bare minor — clearing here silently downgraded every old-gen
     * threshold / stress trigger to an uncovered mini (the 3b corruption
     * repro's exact path). Each collector consumes and clears the flags
     * when it finishes. */
    _emperor_gc_last_collect_site = __builtin_return_address(0);
    if (_gc_generational && !_emperor_gc_want_collect) {
        gc_minor(_gc_stack_cover_poll);
        return;
    }
    gc_collect_auto();
}
