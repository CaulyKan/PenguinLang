/* EmperorPenguin garbage collector — green tea (span heap).
 *
 * Heap layout: small objects (24B GCHeader + body, slot <= 512B) live in
 * 8KiB size-class spans cut from aligned superchunks (gc_span.c); larger
 * objects are malloc blocks on _emperor_gc_allocation_list with the
 * incrementally maintained sorted start-address index (interior-pointer
 * resolution via binary search).
 *
 * Roots: per-function frame descriptors (emitted by LLVMEmitter; bare-ref
 * and ref-map struct slots), global roots (incl. value-class/enum globals
 * as conservative scan regions), the fresh-object
 * quarantine ring, and an always-on conservative main-stack cover (safe in
 * a non-moving collector: a hit is just an extra mark).
 *
 * Collection: single-generation, non-moving, stop-the-world.
 * _emperor_gc_poll (emitted before every call/alloc) drains the heap-goal
 * flag with gc_collect_greentea: precise roots -> gt_drain (span-batched
 * gray/black bitmap marking interleaved with the large-object worklist) ->
 * GT-VERIFY invariants -> sweep (malloc unlink + free, span reclaim) ->
 * unified goal update (ONE budget over the combined malloc+span heap — see
 * gc_update_unified_goal).
 *
 * Env knobs (see also emperor_gc.h): GC_VERIFY, EMPEROR_GC_YOUNG (the
 * unified goal's additive churn slack), EMPEROR_GC_GOAL_FACTOR /
 * EMPEROR_GC_MIN_HEAP, EMPEROR_GC_STATS,
 * GC_PROFILE, EMPEROR_GC_STRESS_EVERY/_MAX, EMPEROR_GC_DISABLE,
 * EMPEROR_GC_HARD_CAP (the allocation pressure valve — see
 * _emperor_gc_alloc).
 *
 * File map: index/resolve (sorted index, gc_resolve_any) -> regions ->
 * frame roots -> quarantine -> stats -> mark/drain (gc_scan_object_body) ->
 * ref-map walk -> sweep -> collect driver -> init -> allocation -> poll.
 * The span heap itself lives in
 * gc_span.c (shared interface: include/gc_internal.h). */
#include "emperor_types.h"
#include "emperor_gc.h"
#include "gc_internal.h"
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdio.h>
#include <time.h>
#include <setjmp.h>
#include <sched.h>
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

/* ---- Unified dual-heap goal ----
 * ONE collection budget for the malloc side AND the span heap (the split
 * goals it replaces are documented in gc_internal.h). */
static size_t _gc_unified_goal = GC_GOAL_MIN_HEAP; /* floors at min_heap */
static size_t _gc_goal_factor = GC_GOAL_FACTOR;    /* EMPEROR_GC_GOAL_FACTOR */
static size_t _gc_goal_min_heap = GC_GOAL_MIN_HEAP;/* EMPEROR_GC_MIN_HEAP */

/* Runtime kill-switch: set EMPEROR_GC_DISABLE=1 in the environment to raise
 * the heap goal to 8TiB so automatic collection never fires (diagnostics —
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

/* ---- Multi-thread interop ----
 * scheduler.c: true while worker threads may be running coroutines (a
 * parallel delta). During that window the allocator funnels through
 * gc_lock and collections are DEFERRED to the delta boundary (where the
 * scheduler parks every worker — stop-the-world by construction). */
extern int _emperor_sched_parallel_active(void);
extern void _emperor_sched_request_collect(void);
#if defined(_WIN32) || defined(EMPEROR_WIN)
/* core_builtin.c's windows.h include order: guard via the sync header. */
#endif
/* scheduler.c: thread-registry registration needs the recorded stack base. */
void* _emperor_gc_stack_base(void) { return _emperor_gc_stack_bottom; }

/* ---- GC Roots ---- */

#define EMPEROR_GC_MAX_ROOTS 65536
static void** _emperor_gc_global_roots[EMPEROR_GC_MAX_ROOTS];
static int _emperor_gc_global_root_count = 0;

/* The allocator funnel's global lock: the small-object fast path never
 * takes it — only span retire/refill, the malloc large path, the
 * registries and the arena registry funnel through; also guards the root
 * registry and the scan region registry, which concurrent spawns/parks
 * mutate). */
#if defined(EMPEROR_WIN) || defined(_WIN32)
/* windows.h comes in via core_builtin.c's build; SRWLOCK static init = {0}. */
static SRWLOCK gc_alloc_mutex = SRWLOCK_INIT;
void gc_alloc_lock(void) { AcquireSRWLockExclusive(&gc_alloc_mutex); }
void gc_alloc_unlock(void) { ReleaseSRWLockExclusive(&gc_alloc_mutex); }
#else
#include <pthread.h>
static pthread_mutex_t gc_alloc_mutex = PTHREAD_MUTEX_INITIALIZER;
void gc_alloc_lock(void) { pthread_mutex_lock(&gc_alloc_mutex); }
void gc_alloc_unlock(void) { pthread_mutex_unlock(&gc_alloc_mutex); }
#endif
static void* gc_alloc_locked(int size, int is_string, GtArena* arena);

void _emperor_gc_add_root(void** root) {
    gc_alloc_lock();
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
    gc_alloc_unlock();
}

/* Unregister a root slot (swap-remove; order is irrelevant — marking walks
 * the whole array). Unregistering an unknown slot is a no-op. Used by the
 * scheduler to drop the async ctx root once the entry consumes it. */
void _emperor_gc_remove_root(void** root) {
    gc_alloc_lock();
    for (int i = 0; i < _emperor_gc_global_root_count; i++) {
        if (_emperor_gc_global_roots[i] == root) {
            _emperor_gc_global_roots[i] = _emperor_gc_global_roots[--_emperor_gc_global_root_count];
            gc_alloc_unlock();
            return;
        }
    }
    gc_alloc_unlock();
}

/* ---- External scan regions ----
 * Raw (non-GC) buffers that CONTAIN pointers to GC objects. Today the only
 * registrant is the coroutine scheduler (its per-coroutine stacks); the
 * conservative stack/object scan cannot see inside malloc'd memory, so the
 * scheduler registers each stack here and marking treats the region as an
 * extension of the stack. */

typedef struct {
    char* base;
    size_t bytes;
    /* Low bound of the LIVE portion of this region ([live_lo, base+bytes) is
     * scanned). For raw buffer registrations it stays == base (everything is
     * live). The coroutine scheduler narrows it to each coroutine's parked
     * stack pointer via _emperor_gc_scan_set_live, so dead frames below a
     * parked/finished sp — pure stale garbage that a conservative scan would
     * pin forever — are excluded (matches the main stack's watermark
     * treatment). The RUNNING coroutine's region is additionally capped at
     * the collect-time sp in _emperor_gc_collect. */
    char* live_lo;
    /* Two-tier M4b: the registering scheduler thread's index — a
     * domain-local collection scans ONLY its own thread's regions (the
     * other domains' coroutine stacks are concurrently live memory;
     * reading them races their writers, which only a full stop may do). */
    int owner_thread;
} GCScanRegion;

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

/* Region-registry lock: mid-delta, several threads touch the registry at
 * once — any thread's co_create registers a stack, any thread's destroy
 * removes one, and every park narrows one. Unlocked, two concurrent adds
 * claimed the same slot and under-counted (an unregistered coroutine stack
 * would not be scanned = premature frees); found by TSan on the mt
 * scheduler, 2026-10-05. The collector itself runs at the all-parked
 * boundary, where no mutator can contend. */
#if defined(EMPEROR_WIN) || defined(_WIN32)
/* SRWLOCK like gc_alloc_mutex above: program links carry no -lwinpthread. */
static SRWLOCK gc_region_mutex = SRWLOCK_INIT;
#define GC_REGION_LOCK() AcquireSRWLockExclusive(&gc_region_mutex)
#define GC_REGION_UNLOCK() ReleaseSRWLockExclusive(&gc_region_mutex)
#else
static pthread_mutex_t gc_region_mutex = PTHREAD_MUTEX_INITIALIZER;
#define GC_REGION_LOCK() pthread_mutex_lock(&gc_region_mutex)
#define GC_REGION_UNLOCK() pthread_mutex_unlock(&gc_region_mutex)
#endif

extern int _emperor_sched_my_thread_index(void);

static void gc_scan_add_locked(void* base, size_t bytes) {
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
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].owner_thread =
        _emperor_sched_my_thread_index();   /* M4b: -1 when no scheduler */
    gc_region_map_put(base, _emperor_gc_scan_region_count);
    _emperor_gc_scan_region_count++;
}

void _emperor_gc_scan_add(void* base, size_t bytes) {
    GC_REGION_LOCK();
    gc_scan_add_locked(base, bytes);
    GC_REGION_UNLOCK();
}

void _emperor_gc_scan_remove(void* base) {
    GC_REGION_LOCK();
    size_t i = gc_region_map_find(base);
    if (i == (size_t)-1) {
        /* No hash index (grow failed) or unregistered base: linear fallback. */
        for (i = 0; i < _emperor_gc_scan_region_count; i++) {
            if (_emperor_gc_scan_regions[i].base == (char*)base) break;
        }
        if (i == _emperor_gc_scan_region_count) {
            GC_REGION_UNLOCK();
            return;
        }
    } else if (i >= _emperor_gc_scan_region_count ||
               _emperor_gc_scan_regions[i].base != (char*)base) {
        /* Stale index (should not happen; the map is re-synced on every
         * move) — fall back to a linear scan rather than freeing nothing. */
        for (i = 0; i < _emperor_gc_scan_region_count; i++) {
            if (_emperor_gc_scan_regions[i].base == (char*)base) break;
        }
        if (i == _emperor_gc_scan_region_count) {
            GC_REGION_UNLOCK();
            return;
        }
    }
    size_t last = _emperor_gc_scan_region_count - 1;
    if (i != last) {
        _emperor_gc_scan_regions[i] = _emperor_gc_scan_regions[last];
        gc_region_map_put(_emperor_gc_scan_regions[i].base, i);
    }
    gc_region_map_del(base);
    _emperor_gc_scan_region_count--;
    GC_REGION_UNLOCK();
}

/* Narrow a registered region's live range (coroutine stacks: live_lo moves to
 * the coroutine's parked sp; raising it past dead frames is the point). */
void _emperor_gc_scan_set_live(void* base, void* live_lo) {
    GC_REGION_LOCK();
    size_t i = gc_region_map_find(base);
    if (i == (size_t)-1 || i >= _emperor_gc_scan_region_count ||
        _emperor_gc_scan_regions[i].base != (char*)base) {
        for (i = 0; i < _emperor_gc_scan_region_count; i++) {
            if (_emperor_gc_scan_regions[i].base == (char*)base) break;
        }
        if (i == _emperor_gc_scan_region_count) {
            GC_REGION_UNLOCK();
            return;
        }
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
    GC_REGION_UNLOCK();
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

/* THREAD-LOCAL (see emperor_gc.h): each thread's main stack links its own
 * chain; the scheduler's per-coroutine swap in _co_switch_in stays
 * thread-private even with N workers switching in parallel. The EMITTED
 * code reaches it through the get/set accessors below — the raw symbol
 * cannot appear in the IR (a thread_local global in a JIT-compiled module
 * resolves to an emutls clone that does not alias the host's ELF TLS;
 * a plain call binds at link/JIT time and reads the true slot). */
_Thread_local void* _emperor_gc_frame_head = NULL;

void* _emperor_gc_frame_get(void) { return _emperor_gc_frame_head; }
void _emperor_gc_frame_set(void* head) { _emperor_gc_frame_head = head; }

/* Set by _emperor_gc_alloc when the heap goal is reached; drained by the
 * __gc_poll calls the emitter places at every call/alloc site. C-side
 * allocations never collect inline in poll mode — collection happens at
 * penguin safepoints where the frame chains are authoritative. */
int _emperor_gc_want_collect = 0;

/* The safepoint poll's one-word fast path (gc_internal.h: the bit layout and
 * the raisers). Zero == nothing to do. */
int _emperor_gc_poll_gate = 0;

/* scheduler.c interop (another TU, so it cannot use the inline raiser): the
 * boundary and _emperor_sched_request_collect both go through here. */
void _emperor_gc_request_collect(void) {
    gc_request_collect();
}

/* Lower the poll gate: WORK off, the env-derived bits stay. The driver is its
 * only steady-state lower site (the disabled guard is the other) — so a poll
 * that finds the gate up always finds a request behind it, and the fast path
 * never has to re-check the flag. */
static inline void gc_lower_poll_gate(void) {
    __atomic_store_n(&_emperor_gc_poll_gate, gc_poll_gate_static_bits(),
                     __ATOMIC_RELAXED);
}

/* EMPEROR_GC_STRESS_MAX reached: stress mode ends, so STRESS comes off the
 * gate and the poll returns to its fast path. WORK is re-derived from the
 * flag so a request raised while stress was live survives the rewrite; losing
 * that race self-heals, because the goal check re-raises on the very next
 * allocation past the goal. */
static void gc_poll_gate_drop_stress(void) {
    __atomic_store_n(&_emperor_gc_poll_gate,
                     gc_poll_gate_static_bits()
                     | (__atomic_load_n(&_emperor_gc_want_collect, __ATOMIC_RELAXED)
                        ? GC_GATE_WORK : 0),
                     __ATOMIC_RELAXED);
}

/* ---- Allocation hard cap: the pressure valve (concurrent_scheduler
 * spec §10.1) ----
 * A delta can be arbitrarily long while collection only happens at the
 * scheduler boundary — under the mt kernel a coroutine that allocates in a
 * tight loop would otherwise grow the heap without bound (polls defer to
 * the boundary while a parallel delta is in flight). The cap bounds it:
 * the allocator refuses the charge, the touching coroutine parks on
 * "wait for GC" (scheduler.c's _emperor_sched_alloc_park — a CO_COND park,
 * NOT runnable, which is exactly what converges an all-blocked delta into
 * the boundary's stop-the-world collection), the collection frees the
 * garbage, the boundary releases the parkers and the allocation retries.
 * All-blocked therefore means "collect now", never deadlock or OOM.
 *
 * Where the valve cannot act — no current coroutine (main-stack policy
 * code), the classic scheduler, the sequential fallback — park returns 0
 * at once and the allocation proceeds with the goal cadence untouched:
 * the cap is advisory wherever the valve cannot act (the boundary, not a
 * flag raised here, is what forces the parker's collection — an
 * unconditional flag would degrade the classic path to a full collect on
 * every poll).
 *
 * EMPEROR_GC_HARD_CAP (bytes): unset = the 8GiB default (far above any
 * real workload — the self-compile peaks near 0.6GiB — while runaway
 * allocation storms park instead of eating the machine); explicit 0 =
 * unlimited. The valve logic itself lives in _emperor_gc_alloc below. */
static size_t _gc_hard_cap = 0;

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
 * allocated since the last collection" retention interacts with the goal
 * growth heuristic on churn-heavy workloads (most young blocks are
 * already-dead string temporaries; retaining them makes every cycle look
 * low-garbage, the goal quadruples, and collections stop — measured 5.6GB
 * vs 1.1GB peak RSS on the compiler self-compile). Blocks older than the
 * ring are judged by reachability alone: they are from completed
 * statements, so they are homed or garbage.
 *
 * The ring lives in the allocating thread's GtArena (gc_span.c pushes on
 * the fast path with the arena in hand; the malloc path pushes via
 * gt_quarantine_push) — a boundary collect can run while several arenas
 * hold in-flight temps, so the driver walks every arena's ring
 * (gc_quarantine_mark / gc_quarantine_drop below). */
static void _emperor_gc_mark_object(void* obj);

static void gc_quarantine_mark(GtArena* a, void* ctx) {
    (void)ctx;
    for (size_t i = 0; i < (size_t)a->quarantine_count; i++) {
        _emperor_gc_mark_object(a->quarantine[i]);
    }
}

static void gc_quarantine_drop(GtArena* a, void* ctx) {
    (void)ctx;
    a->quarantine_count = 0;
    a->quarantine_next = 0;
}

/* ---- Young allocation budget (EMPEROR_GC_YOUNG) ----
 * The unified heap goal's ADDITIVE churn slack on top of the factor x live
 * base (see gc_update_unified_goal): one budget of guaranteed churn
 * between collections, so a ramping live set does not pin the garbage
 * fraction near 1/(1+factor) every cycle. 128MB is the measured knee on
 * the compiler self-compile (budget sweep: 16MB→368s, 32MB→197s, 64MB→143s,
 * 128MB→86s, 256MB→73s, RSS ~2x budget); tight-memory hosts can lower it
 * (floored at 64KiB). */
static size_t _gc_young_budget = 128 * 1024 * 1024; /* EMPEROR_GC_YOUNG */

/* ---- Collector-cost attribution (EMPEROR_GC_STATS=1) ----
 * atexit summary: collection counts, cumulative wall time per phase,
 * allocation totals, peak live bytes. Answers "where do the GC's seconds
 * go" without a profiler: a full's time includes its sweep.
 *
 * GC_PROFILE=1 (the `make gc-bench` harness format): one single-line,
 * machine-parseable summary at exit — fulls, cumulative MARK and SWEEP
 * phases and the total GC wall time, plus allocation counts and the
 * live-peak. Timing counters ride the same _gc_timing gate as
 * EMPEROR_GC_STATS, so unset env is a couple of loads. */
static int _gc_stats_on = 0;
static int _gc_profile_on = 0;
int _gc_timing = 0;                        /* gc_internal.h: gc_span.c charges allocs */
static size_t _gc_st_fulls = 0;
static uint64_t _gc_st_ns_full = 0, _gc_st_ns_sweep = 0, _gc_st_ns_mark = 0;
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
    size_t live =
        __atomic_load_n(&_emperor_gc_total_allocated, __ATOMIC_RELAXED) +
        (size_t)gt_heap_bytes();
    if (live > _gc_st_live_peak) _gc_st_live_peak = live;
}

static void _gc_st_report(void) {
    /* Machine-parseable single line for the gc-bench harness. */
    if (_gc_profile_on) {
        fprintf(stderr,
            "gc_profile: fulls=%zu "
            "mark_ms=%.3f sweep_ms=%.3f gc_total_ms=%.3f "
            "allocs=%zu alloc_bytes=%zu live_peak_bytes=%zu\n",
            _gc_st_fulls,
            _gc_st_ns_mark / 1e6, _gc_st_ns_sweep / 1e6,
            _gc_st_ns_full / 1e6,
            _gc_st_allocs, _gc_st_alloc_bytes, _gc_st_live_peak);
    }
    if (!_gc_stats_on) return;
    fprintf(stderr,
        "gc stats: fulls=%zu (%.3fs, of which sweep %.3fs)\n",
        _gc_st_fulls, _gc_st_ns_full / 1e9, _gc_st_ns_sweep / 1e9);
    fprintf(stderr,
        "gc stats: allocs=%zu (%.2f MiB) live_peak=%.2f MiB polls=%llu\n",
        _gc_st_allocs, _gc_st_alloc_bytes / 1048576.0,
        _gc_st_live_peak / 1048576.0,
        (unsigned long long)__atomic_load_n(&_gc_st_polls, __ATOMIC_RELAXED));
}






/* ---- Phase-3 diagnostics (env-gated, zero cost when unset) ----
 * EMPEROR_GC_STRESS_EVERY=N: every Nth poll collects regardless of the want
 * flag — makes rare precise-root misses (wrong frees) reproduce in one run.
 */
unsigned _emperor_gc_stress_every = 0;   /* gc_internal.h: poll-gate STRESS bit */
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
/* scheduler.c interop: per-thread switch state (the COLLECTING thread's
 * own flag/watermark) and the parked-thread main-stack covers (every OTHER
 * registered thread's [base, boundary watermark) — no-op single-threaded). */
extern int _emperor_sched_am_on_coroutine(void);
extern void* _emperor_sched_main_watermark(void);
extern int _emperor_sched_my_thread_index(void);
extern void* _emperor_sched_my_stack_bottom(void);
extern void _emperor_sched_each_parked_main_stack(void (*visit)(void* bottom, void* top));

static void _emperor_gc_mark_object(void* obj);
static void* gc_resolve_any(void* candidate);
static void gc_refmap_walk(const int32_t* m, int32_t idx, char* base,
                           char* root, int root_size,
                           const char* type_name, int depth);

/* Forensics: which collector phase is walking (set around every refmap walk
 * entry point; printed by gc_refmap_abort). */
static const char* _gc_walk_ctx = "?";
/* Two-tier M2: gc_refmap_walk leaf mode — 1 while gc_promote_body walks a
 * graph (leaves queue unshared objects instead of graying/mark-pushing). */
static int _gc_walk_promote;
static void gc_promote_leaf(void* candidate);
/* Two-tier M3: leaf mode for the GC_VERIFY shared-closure check — leaves
 * resolve their target and ABORT on an unshared one (Inv-S1/S2: a shared
 * object must never point at an unshared object; STW-time only, plan §2.7). */
static int _gc_walk_verify_shared;
static const char* _gc_verify_cur_name = "?";
static void* _gc_verify_cur_user = NULL;
static void gc_verify_shared_leaf(void* candidate);
/* Two-tier M4a: leaf mode for the domain-local marker — leaves dispatch to
 * gc_local_leaf (own span marks via header.marked, shared/foreign skip,
 * large objects traverse). */
static int _gc_walk_local;
static void gc_local_leaf(void* candidate);
/* M4a/M4b prerequisites defined later in this TU: */
static int _emperor_gc_collecting;              /* defined with init below */
static void gc_scan_regions_mark(void* raw_co_sp);
static void gc_scan_regions_mark_local(void* raw_co_sp);
static EMPEROR_NO_ASAN void _emperor_gc_mark_conservative(void* stack_bottom, void* stack_top);
static void _emperor_gc_mark_object_raw(void* obj);

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
                /* One span lookup (resolve+gray merged); the fallback goes
                 * straight to the malloc index. */
                void* candidate = *(void**)(base + off);
                if (_gc_walk_promote) { gc_promote_leaf(candidate); continue; }
                if (_gc_walk_verify_shared) { gc_verify_shared_leaf(candidate); continue; }
                if (_gc_walk_local) { gc_local_leaf(candidate); continue; }
                if (gt_mark_candidate(candidate)) continue;
                void* owner = gc_resolve_block(candidate);
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
            gc_refmap_abort("enum tag outside table", m, type_name, (int32_t)tag);
        }
        int32_t sub = m[idx + 2 + tag];
        if (sub != 0) {
            gc_refmap_walk(m, sub, base + 16, root, root_size, type_name, depth + 1);
        }
    } else if (kind == 3) { /* REFARR: 3, elem_stride, elem_sub — native array
                             * T[]: live count at base+8, elements inline at
                             * base+16, one elem_sub program per element (0 =
                             * pointer-free element = precise no-op). */
        int32_t stride = m[idx + 1];
        int32_t sub = m[idx + 2];
        if (stride <= 0 || sub < 0 || sub >= total) {
            gc_refmap_abort("refarr extent", m, type_name, idx);
        }
        if (sub == 0) return; /* pointer-free elements — nothing to mark */
        int64_t count = *(int64_t*)(base + 8);
        if (count < 0 ||
            (int64_t)16 + count * (int64_t)stride > (int64_t)root_size) {
            gc_refmap_abort("refarr length outside object", m, type_name, idx);
        }
        for (int64_t i = 0; i < count; i++) {
            if (_emperor_gc_mark_failed) return;
            gc_refmap_walk(m, sub, base + 16 + i * stride, root, root_size,
                           type_name, depth + 1);
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

/* Scan ONE object's body: precise ref-map walk when the metadata declares
 * one, else the conservative whole-body word scan (foreign metadata, or a
 * freshly allocated block between zeroing and metaptr stamping — necessary
 * because a field can be a struct that *contains* pointers without the
 * field itself being a single pointer, e.g. `Option<T>` lays out as
 * { ptr metadata, i32 tag, ptr payload }). The child leaf: span-resident
 * children gray their slot (batched in gt_drain), malloc children
 * mark+push the shared worklist. Shared by gc_mark_drain (large path) and
 * the green-tea span/representative scans (gc_span.c). */
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
        /* One span lookup; the fallback is the malloc index directly —
         * gc_resolve_any would re-run the (already failed) span check
         * on every garbage word. */
        void* candidate = ptr[i];
        if (gt_mark_candidate(candidate)) continue;
        void* owner = gc_resolve_block(candidate);
        if (owner) {
            GCHeader* child = (GCHeader*)((char*)owner - sizeof(GCHeader));
            if (!child->marked) {
                child->marked = 1;
                if (!gc_mark_stack_push(child)) { _emperor_gc_mark_failed = 1; return; }
            }
        }
    }
}

/* ---- Two-tier heap M2: write-point promotion (plan §2.9/§2.10) ----
 * _emperor_gc_promote(obj) sets the shared bit over obj's not-yet-shared
 * ref-map closure, stopping at already-shared objects — Inv-S1's monotonic
 * closure makes "stop" sound. Called from the handoff points (channel push,
 * Mutex construction/set, Ownership moves through channels) and from
 * crit_exit (deferred promotion of a critical section's allocations — the
 * object graphs are complete by then, which is what makes construction-
 * phase edges safe). Cold path; the worklist persists across calls. */
int _emperor_gc_region = 1;

static GCHeader** _gc_promote_wl = NULL;
static size_t _gc_promote_wl_top = 0;
static size_t _gc_promote_wl_cap = 0;
static size_t _gc_promoted_total_v = 0;

static int gc_promote_push(GCHeader* h) {
    if (_gc_promote_wl_top == _gc_promote_wl_cap) {
        size_t nc = _gc_promote_wl_cap ? _gc_promote_wl_cap * 2 : 256;
        GCHeader** nw =
            (GCHeader**)realloc(_gc_promote_wl, nc * sizeof(GCHeader*));
        if (!nw) {
            /* Incomplete promotion leaves a cross-color edge behind — loud
             * warning now, GC_VERIFY (M3) catches it in debug builds. */
            fprintf(stderr, "emperor gc: promote worklist OOM — promotion "
                            "INCOMPLETE (cross-color edge possible)\n");
            return 0;
        }
        _gc_promote_wl = nw;
        _gc_promote_wl_cap = nc;
    }
    _gc_promote_wl[_gc_promote_wl_top++] = h;
    return 1;
}

/* Walker leaf in promote mode: resolve the candidate to its owning header
 * (span slot first — O(1) range+magic — then the malloc index) and queue
 * it when not yet shared. Foreign values resolve to nothing and are
 * ignored — the same containment rule as the mark leaves. */
static void gc_promote_leaf(void* candidate) {
    if (!candidate) return;
    GCHeader* h = NULL;
    void* span_user = gt_slot_owner(candidate);
    if (span_user) {
        h = (GCHeader*)((char*)span_user - sizeof(GCHeader));
    } else {
        void* owner = gc_resolve_block(candidate);
        if (owner) h = (GCHeader*)((char*)owner - sizeof(GCHeader));
    }
    if (h && !h->shared) gc_promote_push(h);
}

/* Body walk for promotion: ref-map precise when the metadata declares one,
 * else the conservative word scan with the promote leaf — mirrors
 * gc_scan_object_body's two shapes. */
static EMPEROR_NO_ASAN void gc_promote_body(GCHeader* h) {
    if (h->is_string) return;   /* strings are flat — nothing to traverse */
    char* user = (char*)h + sizeof(GCHeader);
    EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
    const int32_t* map = meta ? meta->refmap : NULL;
    if (map) {
        _gc_walk_ctx = "promote";
        _gc_walk_promote = 1;
        gc_refmap_walk(map, 1, user, user, h->size, meta ? meta->name : NULL, 0);
        _gc_walk_promote = 0;
        return;
    }
    void** ptr = (void**)user;
    size_t word_count = (size_t)h->size / sizeof(void*);
    for (size_t i = 0; i < word_count; i++) gc_promote_leaf(ptr[i]);
}

size_t _emperor_gc_promote(void* obj) {
    if (!_emperor_gc_region || !obj) return 0;
    GCHeader* h = NULL;
    void* span_user = gt_slot_owner(obj);
    if (span_user) {
        h = (GCHeader*)((char*)span_user - sizeof(GCHeader));
    } else {
        void* owner = gc_resolve_block(obj);
        if (owner) h = (GCHeader*)((char*)owner - sizeof(GCHeader));
    }
    if (!h || h->shared) return 0;   /* foreign value, or already shared: stop */
    size_t before = _gc_promoted_total_v;
    size_t base_top = _gc_promote_wl_top;
    if (!gc_promote_push(h)) return 0;
    while (_gc_promote_wl_top > base_top) {
        GCHeader* cur = _gc_promote_wl[--_gc_promote_wl_top];
        if (cur->shared) continue;   /* queued twice before being reached */
        /* Span-resident: gt_promote_slot bumps the span's nshared; malloc
         * blocks set the header directly. */
        if (!gt_promote_slot((char*)cur + sizeof(GCHeader))) cur->shared = 1;
        _gc_promoted_total_v++;
        gc_promote_body(cur);
    }
    return _gc_promoted_total_v - before;
}

size_t _emperor_gc_promoted_total(void) { return _gc_promoted_total_v; }

/* Critical-section bracket around Mutex.with callbacks. Every exit drains
 * the arena's crit list (innermost nesting included — idempotent): each
 * tracked allocation is promoted NOW, still inside the caller's lock
 * (Inv-M2). Promotion never allocates GC objects, so nothing re-enters
 * the list during the drain. */
void _emperor_gc_crit_enter(void) {
    if (!_emperor_gc_region) return;
    GtArena* a = gt_arena_thread();
    if (a) a->crit_depth++;
}

void _emperor_gc_crit_exit(void) {
    if (!_emperor_gc_region) return;
    GtArena* a = gt_arena_thread();
    if (!a || a->crit_depth == 0) return;
    a->crit_depth--;
    if (a->crit_n) {
        void** arr = a->crit_arr;
        size_t n = a->crit_n;
        a->crit_n = 0;
        for (size_t i = 0; i < n; i++) _emperor_gc_promote(arr[i]);
    }
}

/* ---- Two-tier M3: GC_VERIFY shared-closure assertion (Inv-S1/S2) ----
 * STW-time only (the collector driver calls this post-drain, pre-sweep):
 * reading a shared object's fields races other domains' locked writes
 * outside a full stop (plan §2.7). Walks every shared object's body and
 * aborts on any edge resolving to an UNSHARED GC object — a cross-color
 * edge means promotion missed part of a closure (write-point bug).
 * Caveat: mapless bodies (foreign metadata) take the conservative word
 * scan, so an integer word that coincidentally lands on an unshared GC
 * object would false-positive — in practice every emitted type carries a
 * ref-map; debug builds only. */
static void gc_verify_shared_leaf(void* candidate) {
    if (!candidate) return;
    GCHeader* h = NULL;
    void* target_user = NULL;
    void* span_user = gt_slot_owner(candidate);
    if (span_user) {
        target_user = span_user;
        h = (GCHeader*)((char*)span_user - sizeof(GCHeader));
    } else {
        void* owner = gc_resolve_block(candidate);
        if (owner) {
            target_user = owner;
            h = (GCHeader*)((char*)owner - sizeof(GCHeader));
        }
    }
    if (h && !h->shared) {
        fprintf(stderr,
                "emperor gc: GC_VERIFY: Inv-S1/S2 violation: shared %p (%s) "
                "-> UNSHARED %p — promotion missed part of the closure\n",
                _gc_verify_cur_user, _gc_verify_cur_name, target_user);
        abort();
    }
}

static EMPEROR_NO_ASAN void gc_verify_shared_body(GCHeader* h) {
    if (h->is_string) return;
    char* user = (char*)h + sizeof(GCHeader);
    EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
    _gc_verify_cur_user = user;
    _gc_verify_cur_name = meta ? meta->name : "?";
    const int32_t* map = meta ? meta->refmap : NULL;
    if (map) {
        _gc_walk_ctx = "verify-shared";
        _gc_walk_verify_shared = 1;
        gc_refmap_walk(map, 1, user, user, h->size,
                       meta ? meta->name : NULL, 0);
        _gc_walk_verify_shared = 0;
        return;
    }
    void** ptr = (void**)user;
    size_t word_count = (size_t)h->size / sizeof(void*);
    for (size_t i = 0; i < word_count; i++) gc_verify_shared_leaf(ptr[i]);
}

static void _emperor_gc_mark_object_raw(void* obj) {
    if (!obj) return;
    if (gt_gray_object(obj)) return;
    GCHeader* header = (GCHeader*)((char*)obj - sizeof(GCHeader));
    if (header->marked) return;
    header->marked = 1;
    if (gc_mark_stack_push(header)) gc_mark_drain();
}

/* ---- Two-tier M4a: domain-local collection (plan §2.5 / §4 M4) ----
 * Marks via header.marked (NOT the span bitmaps — no cross-collector
 * bitmap discipline needed), roots = the FULL root set with local-
 * dispatch leaves: own-domain unshared span slots mark, shared objects
 * are opaque (Inv-S8), other domains' unshared slots skip (equivalent to
 * exact local roots under Inv-S5), unshared large objects traverse (the
 * frame -> large -> local-slot chain must not break — hole 2). Sweeps
 * only the arena's domain pools + currents. Mutually exclusive with the
 * global collector both directions: entry checks _emperor_gc_collecting,
 * and the global driver spins on the active count before starting. */
static int _gc_local_mode = 0;      /* root-scan leaf dispatch flag */
static GtArena* _gc_local_arena = NULL;
static int _gc_local_active_count = 0;   /* global driver spins on this */
static GCHeader** _gc_local_wl = NULL;
static size_t _gc_local_wl_top = 0, _gc_local_wl_cap = 0;
static GCHeader** _gc_local_marked = NULL;  /* every marked header; cleared before return */
static size_t _gc_local_marked_n = 0, _gc_local_marked_cap = 0;
static int _gc_local_failed = 0;
static unsigned _gc_local_quota_spans = 0;  /* EMPEROR_GC_LOCAL (spans; 0 = off) */
uint64_t _gc_local_cycles = 0, _gc_local_freed_total = 0;

static int gc_local_push(GCHeader* h) {
    if (_gc_local_wl_top == _gc_local_wl_cap) {
        size_t nc = _gc_local_wl_cap ? _gc_local_wl_cap * 2 : 256;
        GCHeader** nw = (GCHeader**)realloc(_gc_local_wl, nc * sizeof(GCHeader*));
        if (!nw) { _gc_local_failed = 1; return 0; }
        _gc_local_wl = nw;
        _gc_local_wl_cap = nc;
    }
    _gc_local_wl[_gc_local_wl_top++] = h;
    if (_gc_local_marked_n == _gc_local_marked_cap) {
        size_t nc = _gc_local_marked_cap ? _gc_local_marked_cap * 2 : 256;
        GCHeader** nm = (GCHeader**)realloc(_gc_local_marked, nc * sizeof(GCHeader*));
        if (!nm) { _gc_local_failed = 1; return 0; }
        _gc_local_marked = nm;
        _gc_local_marked_cap = nc;
    }
    _gc_local_marked[_gc_local_marked_n++] = h;
    return 1;
}

/* Mark-dispatch target: USER is a RESOLVED heap object pointer. Shared ->
 * opaque. Own span vs large object decided by the caller's resolution. */
static void gc_local_mark_obj(void* user) {
    GCHeader* h = (GCHeader*)((char*)user - sizeof(GCHeader));
    if (h->shared || h->marked) return;
    h->marked = 1;
    gc_local_push(h);
}

/* Local leaf: candidate from a root slot or a body walk. */
static void gc_local_leaf(void* candidate) {
    if (!candidate) return;
    void* own = NULL;
    if (gt_span_of_arena(_gc_local_arena, candidate, &own)) {
        gc_local_mark_obj(own);           /* own domain: mark unless shared */
        return;
    }
    /* Not our span: another domain's slot (skip — Inv-S5 says our graph
     * never reaches one; conservative roots may point at them, they stay
     * their owner's business) or a malloc large object (traverse when
     * unshared — hole 2). Span slots never live in the malloc index. */
    void* owner = gc_resolve_block(candidate);
    if (owner) gc_local_mark_obj(owner);
}

static EMPEROR_NO_ASAN void gc_local_body(GCHeader* h) {
    if (h->is_string) return;
    char* user = (char*)h + sizeof(GCHeader);
    EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
    const int32_t* map = meta ? meta->refmap : NULL;
    if (map) {
        _gc_walk_ctx = "local";
        _gc_walk_local = 1;
        gc_refmap_walk(map, 1, user, user, h->size, meta ? meta->name : NULL, 0);
        _gc_walk_local = 0;
        return;
    }
    void** ptr = (void**)user;
    size_t word_count = (size_t)h->size / sizeof(void*);
    for (size_t i = 0; i < word_count; i++) gc_local_leaf(ptr[i]);
}

/* Budget check for the refill slow path (gc_span.c): the count of spans
 * refilled since the last local cycle vs the EMPEROR_GC_LOCAL quota. */
void gc_local_check(GtArena* a) {
    if (!_emperor_gc_region || !_gc_local_quota_spans) return;
    /* Never fire inside a critical section: objects allocated there are
       promoted only at crit_exit — mid-section a fresh object's sole
       referent can be the (shared, opaque) payload, and a local sweep
       would reclaim it (Inv-M2's premise covers the promotion, not the
       window before it). */
    if (a->crit_depth) return;
    if ((unsigned)a->spans_since_local >= _gc_local_quota_spans)
        gc_collect_local();
}

int gc_collect_local(void) {
    if (!_emperor_gc_region || !_gc_local_quota_spans) return 0;    GtArena* a = gt_arena_thread();
    if (!a) return 0;
    if (__atomic_load_n(&_emperor_gc_collecting, __ATOMIC_ACQUIRE)) return 0;
    __atomic_add_fetch(&_gc_local_active_count, 1, __ATOMIC_ACQ_REL);
    if (__atomic_load_n(&_emperor_gc_collecting, __ATOMIC_ACQUIRE)) {
        __atomic_sub_fetch(&_gc_local_active_count, 1, __ATOMIC_ACQ_REL);
        return 0;   /* a global cycle started while we entered */
    }
    /* The large-object index may be STALE (allocations since the last
       global refresh sit in the pending-append list): a large object the
       resolver cannot see gets skipped by gc_local_leaf — and every span
       object reachable ONLY through it dies. This was the self-compile
       SIGSEGV (StringBuilder's grown buffer). Refresh before marking. */
    if (!gc_refresh_sorted()) {
        __atomic_sub_fetch(&_gc_local_active_count, 1, __ATOMIC_ACQ_REL);
        return 0;   /* index rebuild failed: defer to the global cycle */
    }
    _gc_local_mode = 1;
    _gc_local_arena = a;
    _gc_local_wl_top = 0;
    _gc_local_marked_n = 0;
    _gc_local_failed = 0;
    size_t base = _gc_local_wl_top;
    jmp_buf reg_buf;
    setjmp(reg_buf);
    __asm__ volatile("" ::: "memory");

    /* Roots — M4b (per-thread): global roots (leaf rule filters foreign
     * objects), THIS thread's frame chain ONLY (_emperor_gc_frame_head is
     * _Thread_local and already carries the current coroutine's chain when
     * on one — other threads' chains are concurrently-written memory),
     * THIS thread's scan regions, own quarantine ring, own main stack. */
    for (int i = 0; i < _emperor_gc_global_root_count; i++)
        gc_local_leaf(*_emperor_gc_global_roots[i]);
    gc_mark_frame_chain(_emperor_gc_frame_head);
    gc_scan_regions_mark_local(_emperor_gc_get_stack_pointer());
    gc_quarantine_mark(a, NULL);
    /* Local cycle = collect-now semantics for THIS arena's ring (mirrors
     * the explicit global cycle): the live entries were just marked, the
     * cover hands over to the conservative stack scan below. */
    gc_quarantine_drop(a, NULL);
    /* THIS thread's main-stack cover (M4b): the anchor is the thread's
     * own scheduler slot (== _emperor_gc_stack_bottom on the main thread);
     * on a coroutine the live portion is [bottom, watermark) — the
     * coroutine stack itself is covered by its scan region above. */
    {
        char* bottom = (char*)_emperor_sched_my_stack_bottom();
        if (!bottom) bottom = (char*)_emperor_gc_stack_bottom;
        /* On a coroutine the SP belongs to the coroutine's heap stack —
         * the main-stack cover must stop at the thread's watermark (the
         * switch-away point recorded on the MAIN stack), mirroring the
         * global driver; the coroutine stack itself is covered by its
         * frame chain + scan region. */
        char* top = (char*)_emperor_gc_get_stack_pointer();
        if (_emperor_sched_am_on_coroutine() && _emperor_sched_main_watermark()) {
            top = (char*)_emperor_sched_main_watermark();
        }
        if (bottom && top) {
            _emperor_gc_mark_conservative(bottom, top);
        }
    }

    while (_gc_local_wl_top > base && !_gc_local_failed) {
        GCHeader* h = _gc_local_wl[--_gc_local_wl_top];
        gc_local_body(h);
    }
    static int audit_n = 0;
    if (getenv("EMPEROR_GC_LOCAL_AUDIT") && !_gc_local_failed
        && ++audit_n <= 50) {
        /* DIAGNOSTIC: full-process mark into the span bitmaps, then diff
           against the local verdicts — any slot the full mark reaches
           that local marking missed is a ROOT/CLOSURE GAP. Aborts with
           the object's type name; this cycle then skips the sweep. */
        jmp_buf ab; setjmp(ab); __asm__ volatile("" ::: "memory");
        for (int i = 0; i < _emperor_gc_global_root_count; i++) {
            void* o = gc_resolve_any(*(void**)_emperor_gc_global_roots[i]);
            if (o) _emperor_gc_mark_object_raw(o);
        }
        gc_mark_frame_chain(_emperor_gc_frame_head);
        _emperor_sched_each_frame_head(gc_mark_frame_chain);
        gc_scan_regions_mark(_emperor_gc_get_stack_pointer());
        gt_each_arena(gc_quarantine_mark, NULL);
        _emperor_gc_mark_conservative(_emperor_gc_stack_bottom,
                                      _emperor_gc_get_stack_pointer());
        _emperor_sched_each_parked_main_stack(_emperor_gc_mark_conservative);
        gc_mark_drain();
        _gc_local_mode = 0;   /* the diff pass marks in GLOBAL mode */
        gt_audit_local_gap(a);
        gt_reset_marks();
        _gc_local_mode = 1;
        _gc_local_failed = 1;   /* skip this cycle's sweep (diagnostic) */
    }
    size_t freed = 0;
    if (!_gc_local_failed) {
        freed = gt_local_sweep(a);
        /* NO _emperor_gc_total_allocated subtraction here: that counter is
         * the MALLOC side only (span allocations never add to it); the
         * span-side books live in _gt.heap_bytes_total/live_slot_bytes,
         * which gt_local_sweep already folds. This was the LOCAL=8
         * accounting overflow. */
        a->spans_since_local = 0;   /* budget resets on a completed cycle */
        _gc_local_cycles++;
        _gc_local_freed_total += freed;
    }
    /* Reset marked on everything this cycle touched (the sweep already did
     * the span slots; large objects and the failure path need this). */
    for (size_t i = 0; i < _gc_local_marked_n; i++)
        _gc_local_marked[i]->marked = 0;
    _gc_local_mode = 0;
    _gc_local_arena = NULL;
    __atomic_sub_fetch(&_gc_local_active_count, 1, __ATOMIC_ACQ_REL);
    return _gc_local_failed ? 0 : 1;
}

void gc_verify_shared_edges(void) {
    /* malloc side: the allocation list carries the shared bit directly. */
    for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) {
        if (h->shared) gc_verify_shared_body(h);
    }
    /* span side: nshared>0 spans only (the fast-path discriminator). */
    gt_verify_shared_objects(&gc_verify_shared_body);
}

/* Drain the mark worklist. Every child pushed by gc_refmap_walk's mark path
 * (frame-chain struct slots) sits on the stack WITHOUT its body being
 * walked — only this loop expands a marked object's fields into marked
 * children. The root phases that push via the ref-map walker must be
 * followed by a drain, or the pushed object survives while its referents
 * are judged dead. Also drained interleaved by gt_drain (green tea). */
void gc_mark_drain(void) {
    while (_emperor_gc_mark_stack_top > 0) {
        GCHeader* cur = _emperor_gc_mark_stack[--_emperor_gc_mark_stack_top];
        gc_scan_object_body(cur);
    }
}

static EMPEROR_NO_ASAN void _emperor_gc_mark_object(void* obj) {
    if (!obj || _emperor_gc_mark_failed) return;
    /* Two-tier M4a: the local marker reuses every root path through this
     * entry point — dispatch to the local mark instead (own span marks
     * via header.marked, shared/foreign skip, large traverses). */
    if (_gc_local_mode) { gc_local_leaf(obj); return; }
    /* Span-resident objects take the batched bitmap path — the slot grays
     * and gt_drain scans it later; the header is untouched (colors are
     * span-side). MUST precede the malloc tail: a span header interpreted
     * as a malloc block would mark a header the bitmap sweep never reads,
     * and the object would die while "marked". */
    if (gt_gray_object(obj)) return;
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

/* ---- GC Sweep ----
 * Unlink dead blocks from the allocation list (tombstoning them marked==2),
 * compact the sorted index, then free the dead chain. Runs no user code;
 * headers stay readable until the frees (tombstone == 2 is the compaction
 * filter), and control never returns to the mutator in between, so no new
 * allocation can recycle a just-freed address before the index drops it. */
static size_t gc_sweep(void) {
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

/* ---- GC Collect ---- */

/* Non-zero while a collection is running: blocks re-entry from user code
 * calling gc_collect() and makes collect-time allocations defer the next
 * automatic collection. */
static int _emperor_gc_collecting = 0;

/* Re-armed by the driver when it lowers the poll gate (defined with the poll
 * further down; the fast path that used to reset it is gone). */
static int64_t _gc_stw_first_seen;

/* Non-zero for an EXPLICIT collect (penguin `gc_collect()`, gc_torture):
 * "collect now" semantics. Such a cycle drops the fresh-object quarantine
 * (young garbage must die — tests assert gc_info() dropped); any in-flight
 * SSA temporaries stay alive through the always-on conservative stack
 * cover instead. */
static int _emperor_gc_explicit_cycle = 0;

/* ---- Region scanning ----
 * Conservative regions word-scan and mark every word that resolves to a
 * heap object. */

/* Two-tier M4b: the local collector's region scan — same walk as
 * gc_scan_regions_mark but ONLY this thread's regions (the other domains'
 * coroutine stacks are concurrently live memory; reading them races their
 * writers — only a full stop may do that). */
static void gc_scan_regions_mark_local(void* raw_co_sp) {
    int _on_co = _emperor_sched_am_on_coroutine();
    int my = _emperor_sched_my_thread_index();
    for (size_t r = 0; r < _emperor_gc_scan_region_count; r++) {
        GCScanRegion* reg = &_emperor_gc_scan_regions[r];
        if (reg->owner_thread != my) continue;
        char* lo = reg->live_lo;
        if (lo < reg->base) lo = reg->base;
        char* raw_lo = (char*)raw_co_sp;
        char* rend = reg->base + reg->bytes;
        if (_on_co && raw_lo >= reg->base && raw_lo < rend && raw_lo < lo) {
            lo = raw_lo;
        }
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

static void gc_scan_regions_mark(void* raw_co_sp) {
    /* scheduler.c accessors: the flag is the COLLECTING thread's. */
    int _on_co = _emperor_sched_am_on_coroutine();
    for (size_t r = 0; r < _emperor_gc_scan_region_count; r++) {
        GCScanRegion* reg = &_emperor_gc_scan_regions[r];
        char* lo = reg->live_lo;
        if (lo < reg->base) lo = reg->base;
        char* raw_lo = (char*)raw_co_sp;
        char* rend = reg->base + reg->bytes;
        if (_on_co && raw_lo >= reg->base && raw_lo < rend && raw_lo < lo) {
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



/* ---- Collection driver (green tea) ----
 * Single-generation, non-moving, stop-the-world full collection. Root
 * inventory: globals, frame chains incl. parked coroutines,
 * conservative regions (scheduler stacks), quarantine, conservative
 * main-stack cover. Heap side: small objects live in spans (gc_span.c),
 * large objects on the malloc list.
 *
 * The conservative cover is ALWAYS on: in a non-moving collector a
 * conservative hit is merely an extra mark — no pin machinery is needed.
 *
 * Sweep order: malloc sweep (unlink + index compact + free) then span
 * reclaim (clear bits, repool). No user code runs at any point. */
/* scheduler.c per-thread phase accounting (EP_MULTITHREAD_SCHEDULER_
 * PERF_ANALYZE): no-ops unless the analyzer is on AND the mt kernel
 * started; classic-mode collections have no slot to account into. */
extern void _emperor_sched_perf_gc_begin(void);
extern void _emperor_sched_perf_gc_end(void);

static void gc_collect_greentea(void) {
    if (_emperor_gc_disabled) { gc_lower_poll_gate(); return; }
    if (!_emperor_gc_stack_bottom || _emperor_gc_collecting) return;
    /* Two-tier M4: a local collection may be mid-flight on another domain
     * (or this one) — its header.marked usage is exclusive with ours.
     * Wait it out; this wait is part of the global STW arrival latency
     * (plan §2.13). Local collections never allocate GC objects, so the
     * wait is bounded. */
    while (__atomic_load_n(&_gc_local_active_count, __ATOMIC_ACQUIRE))
        sched_yield();
    _emperor_gc_collecting = 1;
    __atomic_fetch_add(&_emperor_gc_collect_count, 1, __ATOMIC_RELAXED);
    uint64_t _gc_st_t0 = 0;
    if (_gc_timing) { _gc_st_t0 = _gc_st_ns_now(); _gc_st_fulls++; }

    jmp_buf _gc_register_buf;
    setjmp(_gc_register_buf);
    __asm__ volatile("" ::: "memory");
    void* raw_co_sp = _emperor_gc_get_stack_pointer();

    /* Large-object interior resolution needs the sorted index (span
     * objects resolve O(1) without it). Refresh failure retains
     * everything this cycle. */
    if (!gc_refresh_sorted()) {
        _emperor_gc_collecting = 0;
        return;
    }

    /* Resolve each root through the heap indices before marking: a global
     * string's literal initializer is a STATIC constant object (@str_N in
     * .data.rel.ro), not a heap block — writing its "header" faults on the
     * RELRO page (and corrupts adjacent constants when the page happens to
     * stay writable — the pre-native-arrays build survived on that layout
     * luck alone). Literal constants outlive the process, so skipping them
     * loses nothing. Exact heap pointers resolve to themselves. */
    for (int i = 0; i < _emperor_gc_global_root_count; i++) {
        void* obj = gc_resolve_any(*(void**)_emperor_gc_global_roots[i]);
        if (obj) {
            _emperor_gc_mark_object(obj);
        }
    }
    gc_mark_frame_chain(_emperor_gc_frame_head);
    _emperor_sched_each_frame_head(gc_mark_frame_chain);
    gc_scan_regions_mark(raw_co_sp);
    gc_mark_drain();

    /* Quarantine (fresh-object cover, per arena): explicit cycles DROP the
     * rings (collect-now semantics; the conservative cover below holds
     * in-flight temps), auto cycles consume them. The mark-failed path
     * keeps them: no sweep ran, the cover carries over. */
    if (_emperor_gc_explicit_cycle) {
        gt_each_arena(gc_quarantine_drop, NULL);
    } else {
        gt_each_arena(gc_quarantine_mark, NULL);
        gt_each_arena(gc_quarantine_drop, NULL);
    }

    /* Conservative main-stack cover (always on — see the driver comment):
     * THIS thread's stack via its own watermark (scheduler.c accessors),
     * then every OTHER registered thread's stack up to its boundary
     * watermark — single-threaded today the second part visits nothing. */
    {
        void* cover_top = raw_co_sp;
        if (_emperor_sched_am_on_coroutine() && _emperor_sched_main_watermark()) {
            cover_top = _emperor_sched_main_watermark();
        }
        _emperor_gc_mark_conservative(_emperor_gc_stack_bottom, cover_top);
    }
    _emperor_sched_each_parked_main_stack(_emperor_gc_mark_conservative);

    /* Green-tea drain: the span FIFO queue (word-differential bitmap
     * scans), the representative fast path, interleaved with the shared
     * large-object worklist. Ends with gray == black globally. */
    gt_drain();
    if (_emperor_gc_verify) gt_verify_invariants();
    if (_emperor_gc_verify && _emperor_gc_region) gc_verify_shared_edges();

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
    size_t freed = gc_sweep();
    gt_sweep_reclaim();
    if (_gc_timing) {
        _gc_st_ns_sweep += _gc_st_ns_now() - _gc_st_mk;
        _gc_st_ns_mark += _gc_st_mk - _gc_st_t0;
    }
    __atomic_sub_fetch(&_emperor_gc_total_allocated, freed, __ATOMIC_RELAXED);

    /* The unified dual-heap goal: one budget recalculated from the
     * COMBINED live/freed numbers of both heaps. */
    gc_update_unified_goal(freed);
    _emperor_gc_collecting = 0;
    __atomic_store_n(&_emperor_gc_want_collect, 0, __ATOMIC_RELAXED);
    /* Drop the poll gate (the env-derived bits stay) and re-arm the STW age
     * clock HERE rather than in the poll's fast path: the clock only means
     * anything while a request is outstanding, and this way the fast path
     * stays a pure read — no store, no cache-line traffic, at all. */
    gc_lower_poll_gate();
    __atomic_store_n(&_gc_stw_first_seen, 0, __ATOMIC_RELAXED);
    if (_gc_st_t0) { _gc_st_ns_full += _gc_st_ns_now() - _gc_st_t0; _gc_st_note_live(); }
}

/* Every collection — poll-triggered, emergency (allocation slow path),
 * explicit (penguin gc_collect()) and the scheduler boundary — goes
 * through this one entry: guards, per-thread GC-phase accounting, cycle. */
static void gc_collect_auto(void) {
    _emperor_sched_perf_gc_begin();
    gc_collect_greentea();
    _emperor_sched_perf_gc_end();
}

void gc_collect_greentea_emergency(void) {
    gc_collect_auto();
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
    /* RELAXED loads: called once per allocation from the lock-free fast
     * path — the counters are heuristic inputs, not invariants (the
     * mutators' writes are lock-held or stop-the-world). */
    return __atomic_load_n(&_emperor_gc_total_allocated, __ATOMIC_RELAXED) +
               (size_t)gt_heap_bytes() >=
           __atomic_load_n(&_gc_unified_goal, __ATOMIC_RELAXED);
}

uint64_t gc_unified_goal_value(void) {
    return (uint64_t)__atomic_load_n(&_gc_unified_goal, __ATOMIC_RELAXED);
}

void gc_update_unified_goal(size_t malloc_freed) {
    size_t live = __atomic_load_n(&_emperor_gc_total_allocated, __ATOMIC_RELAXED) +
                  (size_t)gt_live_bytes();
    size_t freed = malloc_freed + (size_t)gt_last_freed();
    /* goal = max(min_heap, factor x live) + young: the live-proportional
     * base PLUS one young budget of guaranteed churn slack between
     * collections. The slack term is what a RAMPING live set needs: a
     * pure factor x live goal pins the garbage fraction near
     * 1/(1+factor) every cycle, so the collector runs at the ramp
     * integral (bootstrap: 5.13GiB of churn under a 0 -> 280MiB live
     * ramp = 47-59 fulls, each marking the whole live set). With the
     * slack, the per-cycle churn budget is live + young, the count drops
     * to ~O(log), and peak heap stays bounded at (1+factor) x live +
     * young. A doubling on top of the slack would overshoot peak heap to
     * ~4x live on ramp-then-churn profiles (bootstrap RSS 1.8GiB), so
     * no rich branch exists. */
    size_t live_target = live * _gc_goal_factor;
    if (live_target < _gc_goal_min_heap) live_target = _gc_goal_min_heap;
    live_target += gc_young_budget_cap();
    size_t goal = __atomic_load_n(&_gc_unified_goal, __ATOMIC_RELAXED);
    if (live_target > goal) {
        __atomic_store_n(&_gc_unified_goal, live_target, __ATOMIC_RELAXED);
    } else if (freed < live / 4) {
        /* Live-heavy (the LSP recompile shape): freeing almost nothing
         * while each cycle costs O(live blocks) — grow x4, capped. */
        if (goal < (1ULL << 42)) {
            __atomic_store_n(&_gc_unified_goal, goal * 4, __ATOMIC_RELAXED);
        }
    }
}

void gc_set_goal_params(size_t factor, size_t min_heap) {
    if (factor) _gc_goal_factor = factor;
    if (min_heap) _gc_goal_min_heap = min_heap;
    /* The same slack-inclusive base gc_update_unified_goal computes, so
     * the knobs take effect before the first collection. */
    size_t base = _gc_goal_min_heap + gc_young_budget_cap();
    size_t goal = __atomic_load_n(&_gc_unified_goal, __ATOMIC_RELAXED);
    if (goal < base) __atomic_store_n(&_gc_unified_goal, base, __ATOMIC_RELAXED);
}

/* gc_internal.h: the young allocation budget — the unified heap goal's
 * ADDITIVE churn slack on top of the factor x live base. */
size_t gc_young_budget_cap(void) {
    return _gc_young_budget;
}

/* Public entry = the penguin `gc_collect()` builtin (and gc_torture):
 * explicit "collect now" semantics — see _emperor_gc_explicit_cycle. */
EMPEROR_NO_ASAN void _emperor_gc_collect(void) {
    _emperor_gc_explicit_cycle = 1;
    gc_collect_auto();
    _emperor_gc_explicit_cycle = 0;
}

/* ---- GC Init ---- */

void _emperor_gc_init(void* stack_bottom) {
    _emperor_gc_allocation_list = NULL;
    __atomic_store_n(&_emperor_gc_total_allocated, 0, __ATOMIC_RELAXED);
    __atomic_store_n(&_gc_unified_goal, _gc_goal_min_heap, __ATOMIC_RELAXED);
    _emperor_gc_sorted_count = 0;
    _emperor_gc_pending_count = 0;
    _emperor_gc_sorted_stale = 0;
    gt_each_arena(gc_quarantine_drop, NULL); /* arena-side rings */
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
        __atomic_store_n(&_gc_unified_goal, 8ULL << 40, __ATOMIC_RELAXED);
    }
    const char* verify = getenv("GC_VERIFY");
    _emperor_gc_verify = (verify != NULL && verify[0] != '\0' && verify[0] != '0');
    const char* mode = getenv("EMPEROR_GC_MODE");
    if (mode != NULL && mode[0] != '\0') {
        fprintf(stderr,
                "emperor gc: EMPEROR_GC_MODE is obsolete — the green-tea "
                "collector is the only runtime\n");
    }
    const char* young = getenv("EMPEROR_GC_YOUNG");
    if (young != NULL && young[0] != '\0') {
        unsigned long long b = strtoull(young, NULL, 10);
        if (b >= 64 * 1024) _gc_young_budget = (size_t)b;
    }
    /* Two-tier heap master switch: EMPEROR_GC_REGION=0 keeps today's exact
     * single-heap behavior (promotion + crit tracking become no-ops). */
    const char* region = getenv("EMPEROR_GC_REGION");
    if (region != NULL && region[0] != '\0' && region[0] == '0') {
        _emperor_gc_region = 0;
    }
    /* Two-tier M4a: the domain-local collection budget, in SPANS of churn
     * (each unit ~8KiB of allocation). 0 (default for now) = local
     * collection off; the global collector remains the only sweeper. */
    const char* localq = getenv("EMPEROR_GC_LOCAL");
    if (localq != NULL && localq[0] != '\0') {
        unsigned long long q = strtoull(localq, NULL, 10);
        if (q <= 1000000u) _gc_local_quota_spans = (unsigned)q;
    }
    const char* stress = getenv("EMPEROR_GC_STRESS_EVERY");
    if (stress != NULL && stress[0] != '\0') {
        _emperor_gc_stress_every = (unsigned)strtoul(stress, NULL, 10);
    }
    const char* stress_max = getenv("EMPEROR_GC_STRESS_MAX");
    if (stress_max != NULL && stress_max[0] != '\0') {
        _emperor_gc_stress_max = (unsigned)strtoul(stress_max, NULL, 10);
    }
    {
        const char* hcs = getenv("EMPEROR_GC_HARD_CAP");
        if (hcs != NULL && hcs[0] != '\0') {
            _gc_hard_cap = (size_t)strtoull(hcs, NULL, 10); /* 0 = unlimited */
        } else {
            _gc_hard_cap = (size_t)8ULL << 30; /* 8 GiB default */
        }
    }
    /* Unified-goal knobs: factor and min-heap floor. */
    {
        size_t gf = 0, mh = 0;
        const char* gfs = getenv("EMPEROR_GC_GOAL_FACTOR");
        if (gfs != NULL && gfs[0] != '\0') gf = (size_t)strtoull(gfs, NULL, 10);
        const char* mhs = getenv("EMPEROR_GC_MIN_HEAP");
        if (mhs != NULL && mhs[0] != '\0') mh = (size_t)strtoull(mhs, NULL, 10);
        gc_set_goal_params(gf, mh);
    }
    /* Poll gate: the env-derived bits go up once, here, after every knob has
     * been parsed. The WORK bit is raised per-request by gc_request_collect()
     * and lowered by the collect driver. */
    __atomic_store_n(&_emperor_gc_poll_gate, gc_poll_gate_static_bits(),
                     __ATOMIC_RELAXED);
}

/* ---- Runtime ABI version ----
 * The emitted code and the C runtime evolve in lockstep (ref-maps, frame
 * chains). Consumers that mix an emission against a foreign runtime — a
 * stale .penguin-lib, a JIT module from another build — must compare this
 * symbol and refuse loudly instead of misbehaving. Bump on every
 * layout/protocol/symbol-set change below. gt2 dropped the write-barrier
 * symbols; gt3 dropped the typed-region/finalizer symbols
 * (_emperor_gc_track_buffer, _emperor_gc_finalize, destructor slot in
 * EmperorClassMetadata). */
const char* const _emperor_runtime_abi = "emperor-rt-gc3-gt3";

/* ---- GC Info ---- */

uint64_t _emperor_gc_info(void) {
    /* User query: the EXACT figure (unflushed arena deltas included) —
     * e.g. penguin tests read gc_info() around gc_collect() and assert
     * the drop; the approximate per-alloc global would miss everything
     * allocated since the last span refill. */
    return (uint64_t)__atomic_load_n(&_emperor_gc_total_allocated,
                                     __ATOMIC_RELAXED) +
           gt_heap_bytes_exact();
}

int _emperor_gc_probe_tracked(void* user) {
    if (gt_slot_owner(user) == user) return 1;
    return gc_resolve_block(user) == user;
}

int _emperor_gc_sweeping(void) {
    return _emperor_gc_in_sweep;
}

/* Debug split (test-only): the malloc large-object heap's bytes vs the
 * span heap's exact live figure (unflushed arena deltas included). */
void _emperor_gc_info_split(uint64_t* old_bytes, uint64_t* young_bytes) {
    *old_bytes = (uint64_t)__atomic_load_n(&_emperor_gc_total_allocated,
                                            __ATOMIC_RELAXED);
    *young_bytes = gt_heap_bytes_exact();
}

/* The heap charge of a tracked allocation of SIZE body bytes: the
 * size-class slot, or header + round8(size) on the malloc large path.
 * gc_torture's exact live/die delta assertions use this. */
size_t _emperor_gc_alloc_charge(int size) {
    unsigned slot = gt_slot_size_for(size);
    if (slot) return slot;
    int asize = (size + 7) & ~7;
    if (asize < 8) asize = 8;
    return sizeof(GCHeader) + asize;
}


/* ---- GC-tracked Allocation ---- */

size_t _emperor_gc_hard_cap(void) { return _gc_hard_cap; }

/* Live bytes after a collection: the swept malloc list plus the span
 * heap's live slots. (The boundary's OOM check reads this — garbage is
 * gone by then, the quarantine ring's few retained fresh objects are the
 * only overcount, bounded by GC_QUARANTINE_RING entries.) */
uint64_t _emperor_gc_live_bytes(void) {
    return (uint64_t)__atomic_load_n(&_emperor_gc_total_allocated,
                                     __ATOMIC_RELAXED) +
           gt_live_bytes();
}
/* (live_bytes pairs the swept malloc list with span-live; both are exact
 * post-sweep figures, no arena deltas outstanding — plain reads fine.) */

/* Deterministic OOM via the exit() channel (exit code 62): no collection
 * can ever relieve this caller, so unlike the stall leashes (60/61) there
 * is nothing to wait out. On a coroutine this switches away and unwinds
 * through the scheduler's stop path; on main it exits directly. */
extern void _emperor_sched_exit(int code);
void _emperor_gc_oom_abort(const char* why, uint64_t charge) {
    fflush(stdout);
    fprintf(stderr,
            "emperor gc: allocation hard cap exceeded (%s): live %llu bytes + pending %llu bytes, cap %llu bytes\n",
            why,
            (unsigned long long)_emperor_gc_live_bytes(),
            (unsigned long long)charge,
            (unsigned long long)_gc_hard_cap);
    fprintf(stderr,
            "  Raise EMPEROR_GC_HARD_CAP (bytes; 0 = unlimited) or reduce the live set.\n");
    fflush(stderr);
    _emperor_sched_exit(62);
}

/* scheduler.c: park the current coroutine (whose next allocation charges
 * `charge` bytes) until the boundary's post-collection unpark; returns 0
 * when no coroutine can park here. */
extern int _emperor_sched_alloc_park(size_t charge);

/* The boundary's OOM predicate: with the smallest pending parker charge
 * known (scheduler.c tracks the minimum), a live set that cannot admit it
 * can never be relieved by any collection — every retry would just park
 * again. `live > cap` alone is NOT enough: the parkers stall in the gap
 * between "live" and "live + charge" (measured: a keep-live loop parked
 * forever at exactly cap - 48 with every collection freeing nothing). */
int _emperor_gc_cap_blocks(uint64_t charge) {
    return _gc_hard_cap != 0 &&
           _emperor_gc_live_bytes() + charge > (uint64_t)_gc_hard_cap;
}

void* _emperor_gc_alloc(int size, int is_string) {
    if (_gc_hard_cap && !_emperor_gc_collecting) {
        size_t charge = _emperor_gc_alloc_charge(size);
        if (charge > _gc_hard_cap) {
            /* Larger than the cap itself: can never be admitted, not even
             * on an empty heap — no leash applies. */
            _emperor_gc_oom_abort("a single allocation is larger than the cap",
                                  (uint64_t)charge);
        }
        /* The valve reads the APPROXIMATE heap bytes (one relaxed load —
         * this is per-allocation): an arena's unflushed delta is bounded
         * by its current spans' capacity, noise against a GiB-scale cap,
         * and the park loop re-checks after every wake. */
        if ((size_t)(__atomic_load_n(&_emperor_gc_total_allocated,
                                     __ATOMIC_RELAXED) +
                     (size_t)gt_heap_bytes()) +
                charge >
            _gc_hard_cap) {
            /* The valve check sits OUTSIDE the allocator lock: concurrent
             * allocators may each overshoot by one in-flight charge —
             * bounded slack for a valve, not an accounting invariant.
             * Where no coroutine can park (main stack, the classic
             * scheduler, the sequential fallback) park returns 0 at once
             * and the allocation proceeds with the goal cadence untouched
             * — the cap is advisory wherever the valve cannot act (the
             * boundary, not this flag, is what forces the parker's
             * collection). */
            while (_emperor_sched_alloc_park((size_t)charge) &&
                   (size_t)(__atomic_load_n(&_emperor_gc_total_allocated,
                                            __ATOMIC_RELAXED) +
                            (size_t)gt_heap_bytes()) +
                           charge >
                       _gc_hard_cap) {
                /* Parked; resumed by the boundary's unpark after its
                 * stop-the-world collection. Re-check: the freed space
                 * may already be consumed by an earlier parker. */
            }
        }
    }
    /* Resolve this thread's arena BEFORE taking the allocator lock: its
     * first-use registration takes gc_alloc_lock itself, and the malloc
     * path below needs the arena for the quarantine push (the first
     * allocation of a process may be a large one). */
    GtArena* arena = gt_arena_thread();
    /* The span fast path is per-arena and LOCK-FREE (the arena's current
     * span is exclusively owned by this thread; quarantine is pushed with
     * the arena in hand). NULL falls through to the malloc path — a failed
     * superchunk acquisition (deferred to the boundary under a parallel
     * delta, or collected inline otherwise) or an exhausted registry. */
    if (!_emperor_gc_collecting && size <= GT_MAX_BODY) {
        void* small = gt_alloc_small(size, is_string);
        if (small) return small;
    }
    gc_alloc_lock();
    void* r = gc_alloc_locked(size, is_string, arena);
    gc_alloc_unlock();
    /* Goal bookkeeping OUTSIDE the lock (the emergency collect may allocate
     * — it must not re-enter gc_alloc_lock). The fresh block is marked
     * around the collect so the cycle its own allocation triggered cannot
     * sweep it. */
    if (r && !_emperor_gc_disabled && !_emperor_gc_collecting) {
        if (gc_unified_goal_reached()) {
            if (__atomic_load_n(&_emperor_gc_total_allocated, __ATOMIC_RELAXED) +
                    (size_t)gt_heap_bytes() >=
                    __atomic_load_n(&_gc_unified_goal, __ATOMIC_RELAXED) * 8 &&
                !_emperor_sched_parallel_active()) {
                GCHeader* h = (GCHeader*)((char*)r - sizeof(GCHeader));
                h->marked = 1;
                gc_collect_greentea_emergency();
                h->marked = 0;
            } else {
                gc_request_collect();   /* flag + poll gate, kept in step */
            }
        }
    }
    return r;
}

static void* gc_alloc_locked(int size, int is_string, GtArena* arena) {
    /* The malloc "large" path. Small objects allocate on the per-arena
     * lock-free fast path in _emperor_gc_alloc; this runs for them only on
     * fallthrough (superchunk OOM / registry exhaustion) and while
     * collecting (collect-time allocations — the sweep in progress must
     * not judge them via the span bitmaps). */
    int total = (int)sizeof(GCHeader) + size;
    if (_gc_timing) {
        __atomic_add_fetch(&_gc_st_allocs, 1, __ATOMIC_RELAXED);
        __atomic_add_fetch(&_gc_st_alloc_bytes, (size_t)total, __ATOMIC_RELAXED);
    }
    GCHeader* header = (GCHeader*)malloc(total);
    if (!header) return NULL;
    memset(header, 0, total);
    header->next = _emperor_gc_allocation_list;
    header->marked = 0;
    header->is_string = is_string;
    header->size = size;
    header->shared = 0;   /* monotonic bit; M1 keeps it 0 (plan §2.2) */
    _emperor_gc_allocation_list = header;
    __atomic_add_fetch(&_emperor_gc_total_allocated, (size_t)total,
                       __ATOMIC_RELAXED);

    /* Register with the incremental sorted index (see gc_refresh_sorted) so
     * the next collection's interior-pointer resolution sees the block. On
     * append failure mark the index stale — the next refresh falls back to
     * a full rebuild, and until then appends are skipped (nothing can go
     * missing: the rebuild walks the authoritative list). */
    gc_pending_append((char*)header + sizeof(GCHeader));

    /* Two-tier M2: track malloc-path allocations made inside a critical
     * section (same red line as the span fast path: +1 field read). */
    if (_emperor_gc_region && arena && arena->crit_depth)
        gt_crit_track(arena, (char*)header + sizeof(GCHeader));

    gt_quarantine_push(arena, (char*)header + sizeof(GCHeader));

    return (char*)header + sizeof(GCHeader);
}

/* Safepoint poll: called by emitted code before every call and at every
 * allocation site. Cheap when no collection is pending: one load + ret;
 * the heap-goal flag runs the full green-tea cycle.
 *
 * Poll-side STW escalation (free-mt / long deltas): want_collect that the
 * boundary cannot drain — the delta never converges (a busy service) —
 * escalates after EMPEROR_GC_STW_MS (default 250, 0 = off): the polling
 * coroutine files onto the hard-cap valve's parked list, forcing the
 * convergence the boundary needs. Without this, a perpetual delta holds
 * collection off until the heap climbs to the hard cap (8 GiB default)
 * and the VALVE parks the allocators — sound, but the heap sits at the
 * cap instead of the goal. The age clock starts at the FIRST poll that
 * observes the flag (polls are everywhere; a fresh flag is seen within
 * microseconds of being raised) and re-arms whenever the flag is down. */
static int64_t _gc_stw_ms = -1;      /* -1 unparsed */
static int64_t _gc_stw_first_seen = 0; /* ms timestamp, 0 = not pending */
static int64_t _gc_stw_now_ms(void) {
    /* Via _gc_st_ns_now: its Windows path is QPC — clock_gettime maps to
     * winpthread's clock_gettime64 on llvm-mingw (see its comment above). */
    return (int64_t)(_gc_st_ns_now() / 1000000);
}

static void _emperor_gc_poll_slow(void);

void _emperor_gc_poll(void) {
    /* Steady state: ONE relaxed load of the gate + a not-taken branch. Every
     * reason this poll could have to do something (a collection is wanted,
     * stress mode is live, the poll counter is on) is folded into that word,
     * so the fast path reads no other global and writes nothing at all —
     * including the STW age clock, which the driver re-arms instead
     * (gc_internal.h holds the bit layout and the raisers). */
    if (__atomic_load_n(&_emperor_gc_poll_gate, __ATOMIC_RELAXED) == 0) return;
    _emperor_gc_poll_slow();
}

/* The gate is up: a request is outstanding, or the counter/stress bits ask
 * for this call. The original poll body, in the original order. */
static void _emperor_gc_poll_slow(void) {
    /* Every field this touches runs on EVERY coroutine from EVERY thread —
     * plain accesses here are write-write/read-write races by construction
     * (TSan flagged the whole family when the back-edge safepoint made
     * polls ubiquitous). All bookkeeping: RELAXED is the right order. */
    if (_gc_timing) {
        /* Sampled (1 in 4096 poll calls, per thread), reported as its
         * multiple: an exact counter here is one shared RMW per safepoint
         * from every thread — profiling must not change the program it
         * measures. */
        static _Thread_local unsigned _tl_poll_tick;
        if ((++_tl_poll_tick & 0xFFFu) == 0)
            __atomic_fetch_add(&_gc_st_polls, 4096, __ATOMIC_RELAXED);
    }
    if (_emperor_gc_collecting ||
        !_emperor_gc_stack_bottom || _emperor_gc_disabled) {
        return;
    }
    if (_emperor_gc_stress_every) {
        if (_emperor_gc_stress_max &&
            __atomic_load_n(&_emperor_gc_collect_count, __ATOMIC_RELAXED) >= _emperor_gc_stress_max) {
            __atomic_store_n(&_emperor_gc_stress_every, 0, __ATOMIC_RELAXED); /* budget exhausted — normal polls */
            gc_poll_gate_drop_stress();      /* …so drop the gate's STRESS bit */
        } else if (__atomic_fetch_add(&_emperor_gc_stress_counter, 1, __ATOMIC_RELAXED) + 1 >=
                   __atomic_load_n(&_emperor_gc_stress_every, __ATOMIC_RELAXED)) {
            __atomic_store_n(&_emperor_gc_stress_counter, 0, __ATOMIC_RELAXED);
            __atomic_store_n(&_emperor_gc_last_collect_site, __builtin_return_address(0), __ATOMIC_RELAXED);
            gc_request_collect();   /* stress = full collection (flag + gate) */
            if (_emperor_sched_parallel_active()) {
                /* Parallel delta in flight: workers hold coroutine stacks
                 * mid-flight — collecting here would race the mutators.
                 * Defer to the boundary (workers parked = STW). */
                return;
            }
            gc_collect_auto();
        }
        return;
    }
    if (!__atomic_load_n(&_emperor_gc_want_collect, __ATOMIC_RELAXED)) {
        /* The gate outlived the flag: stress mode just ended, or a raiser's
         * two stores were observed out of order. Nothing to do — and the
         * age-clock re-arm that used to live here (with a conditional store,
         * to keep the drained hot path a pure read) moved into the driver,
         * which now resets the clock exactly when it lowers the gate. */
        return;
    }
    if (_emperor_sched_parallel_active()) {
        /* Deferred to the boundary: a parallel delta holds the coroutine
         * stacks, so collecting here would race the mutators. The poll's
         * ONLY job for the whole gate-up window (request raised -> the
         * boundary collect) is the STW escalation, and that check is
         * RATE-LIMITED: the gate stays up for up to EMPEROR_GC_STW_MS, every
         * safepoint on every mutator lands here, and the per-poll clock pair
         * this used to run (plus the diagnostic store) was pure overhead —
         * 4096 polls between checks add far under a millisecond of latency
         * against a 250ms budget. */
        int64_t stw_ms = __atomic_load_n(&_gc_stw_ms, __ATOMIC_RELAXED);
        if (stw_ms < 0) {
            const char* v = getenv("EMPEROR_GC_STW_MS");
            stw_ms = (v && v[0] != '\0') ? atoll(v) : 250;
            __atomic_store_n(&_gc_stw_ms, stw_ms, __ATOMIC_RELAXED);
        }
        if (stw_ms <= 0) return;   /* escalation off: nothing to do per poll */
        static _Thread_local unsigned _tl_poll_age;
        if ((++_tl_poll_age & 0xFFFu) != 0) return;
        if (_gc_timing) {
            /* Collect-site attribution rides the same rate limit. */
            __atomic_store_n(&_emperor_gc_last_collect_site,
                             __builtin_return_address(0), __ATOMIC_RELAXED);
        }
        int64_t first = __atomic_load_n(&_gc_stw_first_seen, __ATOMIC_RELAXED);
        if (first == 0) {
            __atomic_store_n(&_gc_stw_first_seen, _gc_stw_now_ms(), __ATOMIC_RELAXED);
        } else if (_gc_stw_now_ms() - first > stw_ms) {
            extern int _emperor_sched_stw_park(void);
            if (_emperor_sched_stw_park()) {
                /* Resumed by the boundary's post-collection unpark. */
            }
            __atomic_store_n(&_gc_stw_first_seen, 0, __ATOMIC_RELAXED); /* re-arm (flag may still be set —
                                           the next poll re-ages it) */
        }
        return; /* deferred to the boundary (escalation parks + returns) */
    }
    if (_gc_timing) {
        __atomic_store_n(&_emperor_gc_last_collect_site,
                         __builtin_return_address(0), __ATOMIC_RELAXED);
    }
    gc_collect_auto();
}

/* Boundary collection (scheduler.c calls it with every worker parked):
 * drains a deferred request from the parallel window. */
void _emperor_gc_collect_now(void) {
    if (_emperor_gc_disabled || !_emperor_gc_stack_bottom) return;
    if (__atomic_load_n(&_emperor_gc_want_collect, __ATOMIC_RELAXED)) {
        __atomic_store_n(&_emperor_gc_last_collect_site, __builtin_return_address(0), __ATOMIC_RELAXED);
        gc_collect_auto();
    }
}
