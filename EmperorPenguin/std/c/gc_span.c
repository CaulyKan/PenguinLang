/* gc_span.c — green-tea span heap (GC v3).
 *
 * Small objects (24B GCHeader + body, slot <= 512B) allocate from 8KiB
 * SPANS cut from superchunks; one span serves exactly one size class and
 * its slots are bitmap-managed. Larger objects stay on gc.c's malloc block
 * list (the "large" path). Design: docs/impl-notes/25_EmperorPenguinGC.md
 * §4–§8; plan: .agents/plans/gc-greentea-span-heap.md.
 *
 * M5 v1: allocation state is PER-THREAD (GtArena, gc_internal.h) — the
 * per-class current span plus the thread's quarantine ring. The fast path
 * (gt_alloc_small on a current span with a free slot) takes no lock; span
 * retire/refill, superchunk acquisition and the malloc path funnel through
 * gc.c's gc_alloc_lock (once per span fill, not per allocation). The
 * collector stays ONE stop-the-world pass over every arena's spans; the
 * local/shared domain split with promotion at the handoff points (design
 * doc §10.2-10.6) is the next stage.
 *
 * M1 scope: the allocation side plus a per-object mark/sweep adaptation —
 * colors ride the shared GCHeader.marked bit and sweep probes every dead
 * slot for a finalizer. The span-batched gray/black bitmap marker (green
 * tea proper) lands in M2; the gray/black bitmaps below are allocated and
 * held zero for it.
 *
 * Address -> span is O(1): spans are 8KiB-aligned by construction, so the
 * lookup is a range test + mask + magic (the magic guards false hits on
 * malloc arenas that interleave with our superchunks address-wise — the
 * GC v2 _gc_super_ranges lesson).
 */
#include "emperor_gc.h"
#include "gc_internal.h"
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdio.h>

/* scheduler.c: true while worker threads may run coroutines (a parallel
 * delta) — the refill emergency collect must not race live mutators. */
extern int _emperor_sched_parallel_active(void);

/* ---- Layout constants (spec §4.1) ---- */

#define GT_SPAN_SIZE    8192              /* one span, 8KiB-aligned        */
#define GT_SPAN_HDR     128               /* sizeof(GtSpan), fixed         */
#define GT_BITMAP_WORDS 4                 /* 4*64 = 256 slots max          */
#define GT_SUPER_SPANS  128               /* 128 * 8KiB = 1MiB payload     */
#define GT_SUPER_SIZE   (GT_SPAN_SIZE * (GT_SUPER_SPANS + 1))
#define GT_SPAN_MAGIC   0x47545350u       /* "GTSP"                        */
#define GT_SUPER_MAGIC  0x47545355u       /* "GTSU" — must differ: a candi-
                                           * date masking into a super's
                                           * bookkeeping block must fail
                                           * the span magic check          */

/* ---- Span ---- */

#define GT_F_ENQUEUED    0x01   /* M2: span sits in the mark work queue  */
#define GT_F_REP_PENDING 0x02   /* M2: representative fast-path state    */
#define GT_F_REP_HIT     0x04   /* M2: >=2 objects grayed                */

typedef struct GtSpan {
    uint32_t magic;              /* GT_SPAN_MAGIC, guards gt_span_of()    */
    uint8_t  size_class;         /* index into gt_classes[]               */
    uint8_t  flags;              /* GT_F_* (M2)                           */
    uint16_t nslots;             /* slots per span for this class         */
    uint16_t free_index;         /* next slot probe hint (alloc)          */
    uint16_t live_count;         /* allocated (not yet swept-dead) slots  */
    uint16_t rep_slot;           /* M2: representative slot index         */
    uint64_t alloc_bits[GT_BITMAP_WORDS];  /* slot in use                 */
    uint64_t gray_bits[GT_BITMAP_WORDS];   /* M2: reachable, unscanned    */
    uint64_t black_bits[GT_BITMAP_WORDS];  /* M2: reachable, scanned      */
    struct GtSpan* next;         /* pool list link                        */
    struct GtSuper* super;       /* owning superchunk                     */
} GtSpan;

_Static_assert(sizeof(GtSpan) == GT_SPAN_HDR, "GtSpan must fit GT_SPAN_HDR");

/* ---- Size classes (spec §4.2): Go's table up to 512B ---- */

static const struct { uint16_t slot_size; uint16_t nslots; } gt_classes[GT_NCLASS] = {
    {32, 252}, {48, 168}, {64, 126}, {80, 100}, {96, 84},  {112, 72},
    {128, 63}, {144, 56}, {160, 50}, {176, 45}, {192, 42}, {208, 38},
    {224, 36}, {240, 33}, {256, 31}, {288, 28}, {320, 25}, {352, 22},
    {384, 21}, {416, 19}, {448, 18}, {480, 16}, {512, 15},
};

/* slot>>3 -> class index, built once (slot sizes are multiples of 8). */
static uint8_t gt_class_of[GT_MAX_SLOT / 8 + 1];
static int gt_classes_built = 0;

static void gt_build_classes(void) {
    unsigned c = 0;
    for (unsigned slot = 8; slot <= GT_MAX_SLOT; slot += 8) {
        if (gt_classes[c].slot_size < slot && c + 1 < GT_NCLASS) c++;
        gt_class_of[slot >> 3] = (uint8_t)c;
    }
    gt_classes_built = 1;
}

/* ---- Superchunk bookkeeping (first 8KiB of a superchunk) ---- */

typedef struct GtSuper {
    uint32_t magic;              /* GT_SUPER_MAGIC (see the span magic)   */
    uint32_t pad;
    struct GtSuper* next;        /* _gt_supers list (never freed)         */
    void* raw;                   /* malloc base (free target, future)     */
    size_t size;
    uint32_t nspans;             /* GT_SUPER_SPANS                        */
    uint32_t nempty;             /* spans on the class-agnostic pool      */
} GtSuper;

/* ---- Heap state (M5 v1: per-thread arenas + shared pools) ----
 * Allocation state (the per-class CURRENT span) is PER-THREAD (GtArena,
 * registry below) — the fast path in gt_alloc_small touches only the
 * caller's arena. Everything below is shared, mutated either under
 * gc_alloc_lock (retire/refill/super acquire, allocation side) or at the
 * stop-the-world sweep (repool, byte accounting). The collector remains
 * a single STW pass over every arena; the local/shared domain split
 * (design doc §10.2-10.6) is the next stage. */

typedef struct GtHeap {
    GtSpan* partial[GT_NCLASS];  /* swept spans with free slots           */
    GtSpan* full[GT_NCLASS];     /* swept spans, no free slots            */
    GtSpan* empty;               /* class-agnostic; reclassed on demand   */
    GtSuper* supers;
    size_t live_slot_bytes;      /* live slots' bytes (exact post-sweep)  */
    size_t heap_bytes_total;     /* allocated slot bytes: EXACT at sweeps
                                  * (== live), approximate between them —
                                  * each arena folds its alloc_bytes into
                                  * this at its next slow path (once per
                                  * span fill), so the per-alloc readers
                                  * (goal check, hard cap) pay ONE relaxed
                                  * load instead of a registry sum. An
                                  * arena's unflushed delta is bounded by
                                  * what fits in its current spans
                                  * (~23 spans x 8KiB), noise against the
                                  * goal heuristic and the 8GiB cap. */
    /* The heap goal itself moved to gc.c (unified dual-heap budget, spec
     * §9.2): a span-only goal sized to span-live let the malloc side's
     * smaller budget fire cycles that still marked the whole span heap.
     * gt_alloc_small now polls gc_unified_goal_reached(); the goal knobs
     * (factor/min-heap) live in gc.c behind gc_set_goal_params. */
    size_t last_freed;           /* set by gt_sweep_reclaim */
    /* cumulative stats (gc_gt_stats / spec §12) */
    uint64_t n_cycles;           /* completed sweeps                      */
    uint64_t n_wholesale;        /* spans recycled whole                  */
    uint64_t n_rep_scans;        /* representative fast-path scans        */
} GtHeap;

static GtHeap _gt; /* zero-init: live/heap bytes start at 0 */

/* ---- Per-thread arena registry (M5 v1) ----
 * Arenas are process-lifetime (a finished worker's current span stays
 * sweepable — the STW sweep walks the registry, not the live threads).
 * Registration appends to the compact pointer list under gc_alloc_lock;
 * the RELEASE count store keeps the pointer visible to the STW registry
 * walks (sweep/verify/stats). The allocation fast path never touches
 * this — it goes through the TLS pointer only. */

#define GT_MAX_ARENAS 64
static GtArena _gt_arena_pool[GT_MAX_ARENAS];
static GtArena* _gt_arenas[GT_MAX_ARENAS];
static int _gt_narenas = 0;
static __thread GtArena* _gt_tls_arena = NULL;

GtArena* gt_arena_thread(void) {
    GtArena* a = _gt_tls_arena;
    if (a) return a;
    gc_alloc_lock();
    for (int i = 0; i < GT_MAX_ARENAS; i++) {
        if (!_gt_arena_pool[i].used) {
            _gt_arena_pool[i].used = 1;
            _gt_tls_arena = a = &_gt_arena_pool[i];
            _gt_arenas[__atomic_load_n(&_gt_narenas, __ATOMIC_RELAXED)] = a;
            __atomic_store_n(&_gt_narenas, _gt_narenas + 1, __ATOMIC_RELEASE);
            break;
        }
    }
    if (!gt_classes_built) gt_build_classes();
    gc_alloc_unlock();
    return a; /* NULL only with >64 allocating threads (not a supported
               * shape: EMPEROR_THREADS is single-digit) */
}

void gt_each_arena(void (*fn)(GtArena* a, void* ctx), void* ctx) {
    int n = __atomic_load_n(&_gt_narenas, __ATOMIC_ACQUIRE);
    for (int i = 0; i < n; i++) fn(_gt_arenas[i], ctx);
}

/* Fresh-object quarantine ring push (the cover's rationale lives in
 * gc.c's quarantine block). Arena-local: no lock — the ring belongs to
 * the allocating thread, and the collector consumes it only at
 * stop-the-world moments. */
void gt_quarantine_push(GtArena* a, void* user) {
    if (!_gc_greentea) return;
    a->quarantine[a->quarantine_next] = user;
    a->quarantine_next =
        (unsigned short)((a->quarantine_next + 1) % GC_QUARANTINE_RING);
    if (a->quarantine_count < GC_QUARANTINE_RING) a->quarantine_count++;
}
static char* _gt_heap_lo = (char*)(intptr_t)-1;
static char* _gt_heap_hi = (char*)0;
/* Exact superchunk ranges, sorted by start (the GC v2 _gc_super_ranges
 * lesson): the [lo,hi) span covers unmapped gaps between the supers'
 * mmaps, and gt_span_of DEREFERENCES the masked base — a conservative-scan
 * candidate landing in a gap would SIGSEGV on the magic read. Membership
 * in an actual super makes the read safe by construction (supers are
 * never freed, ranges only append). */
static char** _gt_super_ranges = NULL;   /* 2*N: start0, end0, ... */
static size_t _gt_super_range_count = 0;
static size_t _gt_super_range_cap = 0;
static int _gt_ranges_degraded = 0;      /* append failure: lo/hi + magic */
/* Last range that answered a membership hit: marking's reference stream is
 * strongly span/super-local (chains, arrays), so the two-compare re-test
 * skips the binary search for the overwhelming majority of hits. */
static char* _gt_range_cache_lo = NULL;
static char* _gt_range_cache_hi = NULL;

static char* gt_slot_payload(GtSpan* s) {
    return (char*)s + GT_SPAN_HDR;
}

static GCHeader* gt_slot_header(GtSpan* s, unsigned idx) {
    return (GCHeader*)(gt_slot_payload(s) +
                       (size_t)idx * gt_classes[s->size_class].slot_size);
}

static void gt_pool_push(GtSpan** list, GtSpan* s) {
    s->next = *list;
    *list = s;
}

/* ---- Superchunk supply ---- */

/* Over-allocate + align up: no aligned_alloc availability/size-multiple
 * constraints, identical on every platform (cost: <8KiB slack per MiB). */
static void* gt_super_alloc_bytes(size_t size, void** raw_out) {
    void* raw = malloc(size + GT_SPAN_SIZE);
    if (!raw) return NULL;
    *raw_out = raw;
    uintptr_t a = ((uintptr_t)raw + GT_SPAN_SIZE - 1) & ~(uintptr_t)(GT_SPAN_SIZE - 1);
    return (void*)a;
}

static GtSuper* gt_super_acquire(void) {
    void* raw = NULL;
    char* base = (char*)gt_super_alloc_bytes(GT_SUPER_SIZE, &raw);
    if (!base) return NULL;
    GtSuper* su = (GtSuper*)base;
    memset(su, 0, sizeof(GtSuper));
    su->magic = GT_SUPER_MAGIC;
    su->raw = raw;
    su->size = GT_SUPER_SIZE;
    su->nspans = GT_SUPER_SPANS;
    su->next = _gt.supers;
    _gt.supers = su;
    if (base < _gt_heap_lo) _gt_heap_lo = base;
    if (base + GT_SUPER_SIZE > _gt_heap_hi) _gt_heap_hi = base + GT_SUPER_SIZE;
    /* Record the exact range for gt_span_of's safe-membership check. */
    if (!_gt_ranges_degraded) {
        if (_gt_super_range_count == _gt_super_range_cap) {
            size_t new_cap = _gt_super_range_cap ? _gt_super_range_cap * 2 : 32;
            char** grown = (char**)realloc(_gt_super_ranges, new_cap * 2 * sizeof(char*));
            if (!grown) {
                /* Keep the ranges we have; the lookup degrades to the loose
                 * [lo,hi) + magic filter (tiny SEGV exposure on unmapped
                 * gaps, only under allocation failure). */
                _gt_ranges_degraded = 1;
            } else {
                _gt_super_ranges = grown;
                _gt_super_range_cap = new_cap;
            }
        }
        if (!_gt_ranges_degraded &&
            _gt_super_range_count < _gt_super_range_cap) {
            size_t pos = 0;
            while (pos < _gt_super_range_count &&
                   (char*)_gt_super_ranges[2 * pos] < base) pos++;
            memmove(&_gt_super_ranges[2 * (pos + 1)],
                    &_gt_super_ranges[2 * pos],
                    (_gt_super_range_count - pos) * 2 * sizeof(char*));
            _gt_super_ranges[2 * pos] = base;
            _gt_super_ranges[2 * pos + 1] = base + GT_SUPER_SIZE;
            _gt_super_range_count++;
        }
    }
    /* Spans #0..#127 (block 0 is this header). Pushed in REVERSE so the
     * empty pool pops them in ascending address order. */
    for (int i = GT_SUPER_SPANS - 1; i >= 0; i--) {
        GtSpan* s = (GtSpan*)(base + GT_SPAN_SIZE * (i + 1));
        memset(s, 0, sizeof(GtSpan));
        s->super = su;
        gt_pool_push(&_gt.empty, s);
        su->nempty++;
    }
    return su;
}

/* Take a span from the empty pool and (re)class it for CLS. */
static GtSpan* gt_span_class(unsigned cls) {
    GtSpan* s = _gt.empty;
    if (!s) return NULL;
    _gt.empty = s->next;
    s->super->nempty--;
    GtSuper* su = s->super;
    memset(s, 0, sizeof(GtSpan));
    s->magic = GT_SPAN_MAGIC;
    s->size_class = (uint8_t)cls;
    s->nslots = gt_classes[cls].nslots;
    s->super = su;
    return s;
}

/* ---- Address -> span / owner (spec §4.3, §7.1) ---- */

/* Membership in an ACTUAL super (binary search over the exact ranges):
 * makes gt_span_of's deref safe — the masked base of an in-super
 * candidate lies in the same mapped super. */
static int gt_in_super_range(const char* c) {
    if (c < _gt_heap_lo || c >= _gt_heap_hi) return 0;
    if (_gt_ranges_degraded || !_gt_super_ranges) {
        /* No exact table (no supers yet, or append failure): accept the
         * loose span — every span candidate then rides the magic check. */
        return 1;
    }
    if (c >= _gt_range_cache_lo && c < _gt_range_cache_hi) return 1;
    size_t lo = 0, hi = _gt_super_range_count;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if ((char*)_gt_super_ranges[2 * mid + 1] <= c) lo = mid + 1;
        else hi = mid;
    }
    if (lo >= _gt_super_range_count) return 0;
    if (c >= (char*)_gt_super_ranges[2 * lo]) {
        _gt_range_cache_lo = _gt_super_ranges[2 * lo];
        _gt_range_cache_hi = _gt_super_ranges[2 * lo + 1];
        return 1;
    }
    return 0;
}

static GtSpan* gt_span_of(const void* p) {
    const char* c = (const char*)p;
    if (!gt_in_super_range(c)) return NULL;
    GtSpan* s = (GtSpan*)((uintptr_t)c & ~(uintptr_t)(GT_SPAN_SIZE - 1));
    if (s->magic != GT_SPAN_MAGIC) return NULL;
    return s;
}

void* gt_slot_owner(const void* p) {
    GtSpan* s = gt_span_of(p);
    if (!s) return NULL;
    const char* payload = gt_slot_payload(s);
    const char* c = (const char*)p;
    if (c < payload) return NULL; /* points into the span header */
    size_t idx = (size_t)(c - payload) / gt_classes[s->size_class].slot_size;
    if (idx >= s->nslots) return NULL;
    if (!(s->alloc_bits[idx >> 6] & (1ull << (idx & 63)))) return NULL;
    GCHeader* h = gt_slot_header(s, (unsigned)idx);
    if (h->size < 0) return NULL;
    char* user = (char*)(h + 1);
    /* Same containment rule as v2's gc_resolve_block: interior pointers
     * (enum payloads at +16, nested value classes) root their owner;
     * c == user is the exact-base case. */
    if (c < user + h->size) return user;
    return NULL;
}

/* ---- Allocation (spec §6; M5 v1: per-arena fast path) ---- */

static GtSpan* gt_take_span_for(unsigned cls) {
    GtSpan* s = _gt.partial[cls];
    if (s) {
        _gt.partial[cls] = s->next;
        s->next = NULL;
        return s;
    }
    return gt_span_class(cls);
}

/* Slow path: retire ARENA's exhausted current span to the shared full
 * list, then take a fresh span (shared partial -> empty pool -> new
 * super -> emergency collect, in that order). List mutations happen
 * under gc_alloc_lock; the emergency collect runs OUTSIDE it — a
 * finalizer may allocate, and gc_alloc_lock is not recursive (the old
 * single-heap path collected under the lock: a latent deadlock, only
 * unobserved because the path needs a failed superchunk malloc).
 * Mid-parallel-delta the collect is skipped entirely (mutators would
 * race the marker): the flag defers to the boundary and the caller
 * falls back to the malloc path. */
static GtSpan* gt_retire_refill(GtArena* a, unsigned cls) {
    gc_alloc_lock();
    /* Fold this arena's accumulated fast-path bytes into the global
     * counter (once per span fill): keeps gt_heap_bytes a single load. */
    if (a->alloc_bytes) {
        __atomic_add_fetch(&_gt.heap_bytes_total, a->alloc_bytes,
                           __ATOMIC_RELAXED);
        a->alloc_bytes = 0;
    }
    GtSpan* old = a->current[cls];
    if (old) {
        gt_pool_push(&_gt.full[cls], old);
        a->current[cls] = NULL;
    }
    GtSpan* s = gt_take_span_for(cls);
    if (!s) {
        gt_super_acquire(); /* refills the empty pool (or fails) */
        s = gt_take_span_for(cls);
    }
    if (s) {
        s->free_index = 0;
        a->current[cls] = s;
    }
    gc_alloc_unlock();
    if (!s && !_emperor_sched_parallel_active()) {
        gc_collect_greentea_emergency();
        gc_alloc_lock();
        s = gt_take_span_for(cls);
        if (!s && !_gt.empty) {
            if (gt_super_acquire()) s = gt_take_span_for(cls);
        }
        if (s) {
            s->free_index = 0;
            a->current[cls] = s;
        }
        gc_alloc_unlock();
    }
    if (!s) {
        __atomic_store_n(&_emperor_gc_want_collect, 1, __ATOMIC_RELAXED);
    }
    return s;
}

/* Claim one slot in S (the bitmap scan + header stamp + body zero).
 * FROM is the first bitmap word to consider (the fast path starts at
 * free_index, the post-refill retry starts at 0). Returns the user
 * pointer, or NULL when the span has no free slot from FROM onward. */
static void* gt_alloc_in_span(GtArena* a, GtSpan* s, unsigned cls,
                              unsigned from, int size, int is_string) {
    for (unsigned w = from >> 6; w < GT_BITMAP_WORDS; w++) {
        uint64_t freeb = ~s->alloc_bits[w];
        if (!freeb) continue;
        unsigned idx = (w << 6) + (unsigned)__builtin_ctzll(freeb);
        if (idx >= s->nslots) break; /* payload tail of the last word */
        s->alloc_bits[w] |= 1ull << (idx & 63);
        s->free_index = (uint16_t)(idx + 1);
        s->live_count++;
        /* RELAXED add: a plain add on x86-64 — kept atomic-formal because
         * gt_heap_bytes_exact reads other arenas' deltas concurrently. */
        __atomic_add_fetch(&a->alloc_bytes, gt_classes[cls].slot_size,
                           __ATOMIC_RELAXED);
        GCHeader* h = gt_slot_header(s, idx);
        h->next = NULL;
        h->marked = 0;
        h->is_string = is_string;
        h->size = size;
        memset(h + 1, 0, gt_classes[cls].slot_size - sizeof(GCHeader));
        if (_gc_timing) {
            __atomic_add_fetch(&_gc_st_allocs, 1, __ATOMIC_RELAXED);
            __atomic_add_fetch(&_gc_st_alloc_bytes,
                               gt_classes[cls].slot_size, __ATOMIC_RELAXED);
        }
        if (!_emperor_gc_disabled && gc_unified_goal_reached()) {
            /* idempotent until the poll drains */
            __atomic_store_n(&_emperor_gc_want_collect, 1, __ATOMIC_RELAXED);
        }
        gt_quarantine_push(a, (char*)(h + 1));
        return (char*)(h + 1);
    }
    return NULL;
}

void* gt_alloc_small(int size, int is_string) {
    int slot = (int)sizeof(GCHeader) + size;
    slot = (slot + 7) & ~7;
    if (slot < 32) slot = 32;
    if (slot > GT_MAX_SLOT) return NULL;
    unsigned cls = gt_class_of[slot >> 3];
    GtArena* a = gt_arena_thread();
    if (!a) return NULL;
    GtSpan* s = a->current[cls];
    if (!s) {
        s = gt_retire_refill(a, cls);
        if (!s) return NULL;
    }
    void* r = gt_alloc_in_span(a, s, cls, s->free_index, size, is_string);
    if (r) return r;
    /* Current span exhausted: retire+refill under the lock, retry once
     * from word 0 (the fresh span's free_index is 0 anyway). */
    s = gt_retire_refill(a, cls);
    if (!s) return NULL;
    return gt_alloc_in_span(a, s, cls, 0, size, is_string);
}

/* ---- Green-tea mark state (M2, spec §7) ----
 * Work items are SPANS, not objects: a slot's color lives in its span's
 * bitmaps (white = neither bit, gray = reachable/unscanned, black =
 * reachable/scanned). The FIFO queue holds spans with >= 2 gray candidates
 * (enqueued-deduped); spans with exactly ONE gray object sit on the
 * representative array and get a direct single-object scan at drain time
 * (a second distinct object flips REP_HIT and enqueues the span for a real
 * bitmap scan). The representative bookkeeping uses an ARRAY, not the
 * spec's linked list: during marking a span's ->next is its POOL link
 * (current/partial/full), so it cannot double as a mark-structure link. */

static GtSpan** _gt_queue = NULL;        /* FIFO ring of enqueued spans */
static size_t _gt_queue_cap = 0;
static size_t _gt_queue_head = 0;
static size_t _gt_queue_count = 0;
static GtSpan** _gt_rep = NULL;          /* representative candidates */
static size_t _gt_rep_head = 0;          /* consumed up to here per cycle */
static size_t _gt_rep_count = 0;

static int gt_ptr_grow(GtSpan*** arr, size_t* cap, size_t need) {
    if (need <= *cap) return 1;
    size_t new_cap = *cap ? *cap * 2 : 256;
    while (new_cap < need) new_cap *= 2;
    GtSpan** grown = (GtSpan**)realloc(*arr, new_cap * sizeof(GtSpan*));
    if (!grown) return 0;
    *arr = grown;
    *cap = new_cap;
    return 1;
}

static int gt_enqueue(GtSpan* s) {
    if (_gt_queue_count + 1 > _gt_queue_cap) {
        size_t old_cap = _gt_queue_cap;
        size_t old_head = _gt_queue_head;
        if (!gt_ptr_grow(&_gt_queue, &_gt_queue_cap, _gt_queue_count + 1)) {
            return 0;
        }
        if (old_cap) {
            /* Unwrap on grow: entries written under the OLD modulus sit at
             * [head..old_cap) ∪ [0..W); after the cap doubles, (head+count)
             * modulo the NEW cap points past old_cap — the wrapped prefix
             * must move to the fresh tail or the ring's read order breaks
             * (dequeue walked into uninitialized slots — the pass3
             * self-compile crash under the greentea default). */
            size_t live_end = old_head + _gt_queue_count;
            if (live_end > old_cap) {
                size_t w = live_end - old_cap;
                memmove(&_gt_queue[old_cap], &_gt_queue[0],
                        w * sizeof(GtSpan*));
            }
        }
    }
    _gt_queue[(_gt_queue_head + _gt_queue_count) % _gt_queue_cap] = s;
    _gt_queue_count++;
    s->flags |= GT_F_ENQUEUED;
    return 1;
}

static GtSpan* gt_dequeue(void) {
    if (_gt_queue_count == 0) return NULL;
    GtSpan* s = _gt_queue[_gt_queue_head];
    _gt_queue_head = (_gt_queue_head + 1) % _gt_queue_cap;
    _gt_queue_count--;
    s->flags &= (uint8_t)~GT_F_ENQUEUED;
    return s;
}

static int gt_rep_push(GtSpan* s) {
    /* Append-only per cycle (consumed by head index in gt_drain); the
     * array may hold HIT-promoted entries — the drain skips them. */
    static size_t rep_cap = 0;
    if (!gt_ptr_grow(&_gt_rep, &rep_cap, _gt_rep_count + 1)) return 0;
    _gt_rep[_gt_rep_count++] = s;
    return 1;
}

/* Gray one slot: dedup + representative/queue bookkeeping (spec §7.2). */
static void gt_gray_slot(GtSpan* s, unsigned idx) {
    uint64_t bit = 1ull << (idx & 63);
    unsigned w = idx >> 6;
    if (s->gray_bits[w] & bit) return;      /* already gray */
    s->gray_bits[w] |= bit;
    if (s->flags & GT_F_ENQUEUED) return;   /* queued: the drain will see it */
    if (s->flags & GT_F_REP_PENDING) {
        if (s->rep_slot == idx) return;
        s->flags |= GT_F_REP_HIT;           /* 2nd distinct object: real scan */
        if (!gt_enqueue(s)) _emperor_gc_mark_failed = 1;
        return;
    }
    /* Fresh representative candidate. REP_HIT from an EARLIER re-gray
     * cycle (post-dequeue) must clear, or the drain would skip a freshly
     * pended candidate as already-promoted. */
    s->rep_slot = (uint16_t)idx;
    s->flags = (uint8_t)((s->flags & ~GT_F_REP_HIT) | GT_F_REP_PENDING);
    if (!gt_rep_push(s)) _emperor_gc_mark_failed = 1;
}

/* Root/child entry from gc.c: gray OBJ's slot if it is span-resident.
 * Returns 1 when the object lives in a span (caller must not touch its
 * header), 0 when it is a malloc large-path block. OBJ must be an exact
 * slot base (the gc_resolve_* contract). */
int gt_gray_object(void* obj) {
    GtSpan* s = gt_span_of(obj);
    if (!s) return 0;
    char* payload = gt_slot_payload(s);
    size_t slot_size = gt_classes[s->size_class].slot_size;
    size_t off = (size_t)((char*)obj - payload);
    /* OBJ is a USER pointer: slot header at slot_base, body at +24. */
    if (off < sizeof(GCHeader) || (off - sizeof(GCHeader)) % slot_size != 0 ||
        (off - sizeof(GCHeader)) / slot_size >= s->nslots) {
        return 1; /* defensive: a non-base cannot happen via resolve; a
                   * no-op beats misinterpreting payload as a header */
    }
    gt_gray_slot(s, (unsigned)((off - sizeof(GCHeader)) / slot_size));
    return 1;
}

/* Combined resolve+gray for the mark leaves (spec §7.1): ONE range lookup
 * decides span vs large — the resolve-then-gray pairing would pay the
 * super-range binary search twice per reference word otherwise (the M2
 * profile: gt_slot_owner + gt_gray_object's re-resolve dominated mark). */
int gt_mark_candidate(void* candidate) {
    GtSpan* s = gt_span_of(candidate);
    if (!s) return 0;
    char* payload = gt_slot_payload(s);
    size_t slot_size = gt_classes[s->size_class].slot_size;
    size_t idx = (size_t)((const char*)candidate - payload) / slot_size;
    if (idx >= s->nslots) return 0;
    if (!(s->alloc_bits[idx >> 6] & (1ull << (idx & 63)))) return 0;
    GCHeader* h = gt_slot_header(s, (unsigned)idx);
    if (h->size < 0) return 0;
    char* user = (char*)(h + 1);
    /* Same containment rule as gt_slot_owner: interior pointers root
     * their owner; candidate == user is the exact-base case. */
    if ((const char*)candidate >= user + h->size) return 0;
    gt_gray_slot(s, (unsigned)idx);
    return 1;
}

/* Full bitmap scan of one span: blacken `gray & ~black` word-at-a-time
 * and scan exactly those objects (spec §7.3). Scanning can gray NEW slots
 * in this same span — the outer loop re-passes until gray == black. */
static void gt_scan_span(GtSpan* s) {
    s->flags &= (uint8_t)~GT_F_REP_PENDING; /* (if HIT promoted it) */
    for (;;) {
        int progressed = 0;
        for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
            uint64_t fresh = s->gray_bits[w] & ~s->black_bits[w];
            if (!fresh) continue;
            s->black_bits[w] |= fresh;      /* blacken the batch */
            while (fresh) {
                unsigned idx = (w << 6) + (unsigned)__builtin_ctzll(fresh);
                fresh &= fresh - 1;
                if (idx >= s->nslots) { fresh = 0; break; }
                gc_scan_object_body(gt_slot_header(s, idx));
                if (_emperor_gc_mark_failed) return;
            }
            progressed = 1;
        }
        if (!progressed) break;
    }
}

/* Drain all mark work: the large-object worklist first (its walks gray
 * span slots), then queued spans, then lone representatives (spec §7.3).
 * The mutator is stopped — no new gray bits can appear once all three
 * structures are empty. */
void gt_drain(void) {
    for (;;) {
        if (_emperor_gc_mark_failed) break;
        if (_emperor_gc_mark_stack_top > 0) {
            gc_mark_drain();
            continue;
        }
        GtSpan* s = gt_dequeue();
        if (s) {
            gt_scan_span(s);
            continue;
        }
        GtSpan* rep = NULL;
        while (_gt_rep_head < _gt_rep_count) {
            GtSpan* c = _gt_rep[_gt_rep_head++];
            if (c->flags & GT_F_REP_HIT) continue; /* the queue scanned it */
            rep = c;
            break;
        }
        if (!rep) break;
        rep->flags &= (uint8_t)~GT_F_REP_PENDING;
        unsigned idx = rep->rep_slot;
        rep->black_bits[idx >> 6] |= 1ull << (idx & 63);
        _gt.n_rep_scans++;
        gc_scan_object_body(gt_slot_header(rep, idx));
    }
    gc_mark_drain(); /* final large drain (or the failure-path cleanup) */
    _gt_queue_head = 0;
    _gt_queue_count = 0;
    _gt_rep_head = 0;
    _gt_rep_count = 0;
}

/* GC_VERIFY span invariants post-drain: gray == black per word (nothing
 * left unscanned) and black ⊆ alloc (no phantom live slot). A violation
 * is a collector bug — loud abort, never a silent mis-sweep. */
void gt_verify_invariants(void) {
    int n = _gt_narenas;
    for (unsigned cls = 0; cls < GT_NCLASS; cls++) {
        GtSpan* lists[2] = {_gt.full[cls], _gt.partial[cls]};
        for (int L = 0; L < 2; L++) {
            for (GtSpan* s = lists[L]; s; s = s->next) {
                for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
                    if (s->gray_bits[w] != s->black_bits[w] ||
                        (s->black_bits[w] & ~s->alloc_bits[w])) {
                        fprintf(stderr,
                                "emperor gc: GT-VERIFY: span %p cls=%u word %u "
                                "gray=%016llx black=%016llx alloc=%016llx\n",
                                (void*)s, cls, w,
                                (unsigned long long)s->gray_bits[w],
                                (unsigned long long)s->black_bits[w],
                                (unsigned long long)s->alloc_bits[w]);
                        abort();
                    }
                }
            }
        }
        for (int i = 0; i < n; i++) {
            GtSpan* cur = (GtSpan*)_gt_arenas[i]->current[cls];
            if (cur) {
                for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
                    if (cur->gray_bits[w] != cur->black_bits[w] ||
                        (cur->black_bits[w] & ~cur->alloc_bits[w])) {
                        fprintf(stderr,
                                "emperor gc: GT-VERIFY: current span %p cls=%u "
                                "word %u gray=%016llx black=%016llx alloc=%016llx\n",
                                (void*)cur, cls, w,
                                (unsigned long long)cur->gray_bits[w],
                                (unsigned long long)cur->black_bits[w],
                                (unsigned long long)cur->alloc_bits[w]);
                        abort();
                    }
                }
            }
        }
    }
}

/* ---- Sweep (spec §8) ---- */

/* Walk every LIVE span: the shared full/partial pools plus every
 * registered arena's current span. Stop-the-world moment only (a span
 * is exclusively either pooled or one arena's current). */
static void gt_foreach_span(void (*fn)(GtSpan*)) {
    int n = _gt_narenas;
    for (unsigned cls = 0; cls < GT_NCLASS; cls++) {
        GtSpan* lists[2] = {_gt.full[cls], _gt.partial[cls]};
        for (int L = 0; L < 2; L++) {
            for (GtSpan* s = lists[L]; s; s = s->next) fn(s);
        }
        for (int i = 0; i < n; i++) {
            GtSpan* c = (GtSpan*)_gt_arenas[i]->current[cls];
            if (c) fn(c);
        }
    }
}

/* Sweep phase 1: run finalizers on dead slots (alloc & ~black), one
 * word-at-a-time dead-bit iteration. No state change — dead bodies stay
 * intact for the malloc sweep's own finalizer pass, which runs between
 * this and gt_sweep_reclaim (see the driver's sweep-order note). */
static void gt_finalize_span(GtSpan* s) {
    for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
        uint64_t dead = s->alloc_bits[w] & ~s->black_bits[w];
        while (dead) {
            unsigned idx = (w << 6) + (unsigned)__builtin_ctzll(dead);
            if (idx >= s->nslots) return;
            dead &= dead - 1;
            _emperor_gc_finalize(gt_slot_header(s, idx));
        }
    }
}

void gt_sweep_finalize(void) {
    gt_foreach_span(gt_finalize_span);
}

static void gt_reset_marks_span(GtSpan* s) {
    for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
        s->gray_bits[w] = 0;
        s->black_bits[w] = 0;
    }
    s->flags = 0;
}

void gt_reset_marks(void) {
    gt_foreach_span(gt_reset_marks_span);
}

/* Sweep one span: black == live at this point. alloc &= black drops the
 * dead wholesale (only the finalizer probe above touched them), colors
 * reset, live recomputed; repool to empty (wholesale recycle) / partial /
 * full. */
static void gt_repool_swept(GtSpan* s) {
    unsigned live = 0;
    for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
        s->alloc_bits[w] &= s->black_bits[w];
        s->gray_bits[w] = 0;
        s->black_bits[w] = 0;
        live += (unsigned)__builtin_popcountll(s->alloc_bits[w]);
    }
    s->live_count = (uint16_t)live;
    s->free_index = 0;
    s->flags = 0;
    if (live == 0) {
        /* Wholesale reclamation: nothing per-slot left to do — the header
         * alone survives (cleared) for reclassing. */
        _gt.n_wholesale++;
        GtSuper* su = s->super;
        memset(s, 0, sizeof(GtSpan));
        s->super = su;
        gt_pool_push(&_gt.empty, s);
        su->nempty++;
    } else {
        _gt.live_slot_bytes +=
            (size_t)live * gt_classes[s->size_class].slot_size;
        s->next = NULL;
        gt_pool_push(live >= s->nslots ? &_gt.full[s->size_class]
                                       : &_gt.partial[s->size_class],
                     s);
    }
}

void gt_sweep_reclaim(void) {
    /* The pre-sweep heap bytes snapshot: fold every arena's unflushed
     * fast-path delta into the global first (STW makes it exact). */
    int n = _gt_narenas;
    for (int i = 0; i < n; i++) {
        __atomic_add_fetch(&_gt.heap_bytes_total, _gt_arenas[i]->alloc_bytes,
                           __ATOMIC_RELAXED);
        _gt_arenas[i]->alloc_bytes = 0;
    }
    size_t pre = __atomic_load_n(&_gt.heap_bytes_total, __ATOMIC_RELAXED);
    _gt.live_slot_bytes = 0;
    for (unsigned cls = 0; cls < GT_NCLASS; cls++) {
        GtSpan* lists[2] = {_gt.full[cls], _gt.partial[cls]};
        _gt.full[cls] = NULL;
        _gt.partial[cls] = NULL;
        for (int L = 0; L < 2; L++) {
            GtSpan* s = lists[L];
            while (s) {
                GtSpan* nx = s->next;
                gt_repool_swept(s);
                s = nx;
            }
        }
        for (int i = 0; i < n; i++) {
            GtSpan* s = (GtSpan*)_gt_arenas[i]->current[cls];
            if (s) {
                _gt_arenas[i]->current[cls] = NULL;
                gt_repool_swept(s);
            }
        }
    }
    __atomic_store_n(&_gt.heap_bytes_total, _gt.live_slot_bytes,
                     __ATOMIC_RELAXED);
    _gt.last_freed = pre - _gt.live_slot_bytes;
    _gt.n_cycles++;
}

/* The span-heap goal heuristic (gt_update_goal/gt_set_goal_params) was
 * retired with the unified dual-heap budget: see gc_internal.h and
 * gc_update_unified_goal in gc.c. */

uint64_t _emperor_gc_gt_stats(int which) {
    switch (which) {
    case 0: return (uint64_t)_gt.live_slot_bytes;
    case 1: return gc_unified_goal_value();
    case 2: { /* full spans */
        uint64_t n = 0;
        int na = _gt_narenas;
        for (unsigned cls = 0; cls < GT_NCLASS; cls++) {
            for (GtSpan* s = _gt.full[cls]; s; s = s->next) n++;
            for (int i = 0; i < na; i++) {
                GtSpan* c = (GtSpan*)_gt_arenas[i]->current[cls];
                if (c && c->live_count >= c->nslots) n++;
            }
        }
        return n;
    }
    case 3: { /* partial spans */
        uint64_t n = 0;
        int na = _gt_narenas;
        for (unsigned cls = 0; cls < GT_NCLASS; cls++) {
            for (GtSpan* s = _gt.partial[cls]; s; s = s->next) n++;
            for (int i = 0; i < na; i++) {
                GtSpan* c = (GtSpan*)_gt_arenas[i]->current[cls];
                if (c && c->live_count < c->nslots) n++;
            }
        }
        return n;
    }
    case 4: { /* empty spans */
        uint64_t n = 0;
        for (GtSpan* s = _gt.empty; s; s = s->next) n++;
        return n;
    }
    case 5: return _gt.n_wholesale;
    case 6: return _gt.n_rep_scans;
    case 7: return _gt.n_cycles;
    case 8: { /* supers */
        uint64_t n = 0;
        for (GtSuper* su = _gt.supers; su; su = su->next) n++;
        return n;
    }
    default: return 0;
    }
}

uint64_t gt_heap_bytes(void) {
    /* One relaxed load (per-allocation reader). Exact at sweeps,
     * approximate between them — see GtHeap.heap_bytes_total. */
    return (uint64_t)__atomic_load_n(&_gt.heap_bytes_total, __ATOMIC_RELAXED);
}

uint64_t gt_heap_bytes_exact(void) {
    /* The exact figure (global + every arena's unflushed delta) for USER
     * queries (gc_info) and boundary predicates — rare, never per-alloc.
     * Racy against concurrent fast paths by nature (each delta read once,
     * RELAXED); the sum is monotonic between sweeps, which is all the
     * queries need. */
    int n = __atomic_load_n(&_gt_narenas, __ATOMIC_ACQUIRE);
    uint64_t sum = (uint64_t)__atomic_load_n(&_gt.heap_bytes_total,
                                             __ATOMIC_RELAXED);
    for (int i = 0; i < n; i++) {
        sum += __atomic_load_n(&_gt_arenas[i]->alloc_bytes, __ATOMIC_RELAXED);
    }
    return sum;
}

uint64_t gt_live_bytes(void) {
    return (uint64_t)_gt.live_slot_bytes;
}

uint64_t gt_last_freed(void) {
    return (uint64_t)_gt.last_freed;
}

unsigned gt_slot_size_for(int size) {
    if (!gt_classes_built) gt_build_classes();
    int slot = (int)sizeof(GCHeader) + size;
    slot = (slot + 7) & ~7;
    if (slot < 32) slot = 32;
    if (slot > GT_MAX_SLOT) return 0;
    return gt_classes[gt_class_of[slot >> 3]].slot_size;
}
