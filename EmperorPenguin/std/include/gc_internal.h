#ifndef EMPEROR_GC_INTERNAL_H
#define EMPEROR_GC_INTERNAL_H

/* Shared internals of the GC runtime's two translation units: gc.c (root
 * discovery, mark core, large-object malloc heap, the collect driver) and
 * gc_span.c (the green-tea span heap). NOT part of the public runtime
 * API (emperor_gc.h) — everything here is internal coupling between the
 * two units and may change freely with the collector. */

#include <stdint.h>
#include <stddef.h>

/* ---- Object header (both heaps) ---- */

typedef struct GCHeader {
    struct GCHeader* next;
    int marked;
    int is_string;
    int size;
    /* Two-tier heap (gc-two-tier-heap plan M1): set at the handoff/write
     * points only, MONOTONIC (never cleared — demotion is rejected, plan
     * D6). Eats the struct's 4B padding so the header stays 24B. Objects
     * with this bit are opaque leaves to the local collector: never freed,
     * never traversed by it (plan Inv-S8). */
    int shared;
} GCHeader;

/* Layout guard: _emperor_gc_alloc_charge() and gc_torture's exact live/die
 * delta assertions account every object with a 24B header — growing it
 * invalidates all of that bookkeeping. */
_Static_assert(sizeof(GCHeader) == 24, "GCHeader must stay 24 bytes");

/* ---- Span-heap limits (layout constants shared by both units) ---- */

/* Largest slot served by a span; a 24B header + body over this takes the
 * malloc "large" path in _emperor_gc_alloc. */
#define GT_MAX_SLOT 512
#define GT_MAX_BODY ((int)(GT_MAX_SLOT - sizeof(GCHeader)))

/* Size-class count and the quarantine ring size: GtArena (below) carries
 * one of each, so the constants live here; gc_span.c/gc.c static-assert
 * their local tables against them. */
#define GT_NCLASS 23
#define GC_QUARANTINE_RING 512

/* Unified-goal defaults: the floor (EMPEROR_GC_MIN_HEAP=0
 * keeps this) and the live multiplier (EMPEROR_GC_GOAL_FACTOR=0 keeps
 * this), applied to the COMBINED malloc+span live set. */
#define GC_GOAL_MIN_HEAP (4u * 1024 * 1024)
#define GC_GOAL_FACTOR 2

/* ---- gc.c state used by gc_span.c ---- */

extern int _gc_timing;               /* stats/profile counters active */
extern size_t _gc_st_allocs, _gc_st_alloc_bytes;
extern int _emperor_gc_disabled;
extern int _emperor_gc_mark_failed;  /* set by either unit on mark OOM */
extern size_t _emperor_gc_mark_stack_top; /* shared worklist depth (gt_drain) */

/* ---- Safepoint poll gate ----
 * The emitted poll runs before every call and at every loop back-edge, so
 * its steady state must cost as close to nothing as possible: ONE relaxed
 * load of a word that is zero. Every condition that could make a poll do
 * work is folded into that word — before this, the same poll read five
 * separate globals, and the profiler put it at 6.3% of the compiler
 * self-compile's wall time. Non-zero routes to gc.c's slow path.
 *
 * Writers: gc_request_collect() below (a collection is wanted) and gc.c's
 * init (the env-derived TIMING/STRESS bits). The collect driver lowers the
 * word once per cycle; a mark-failed cycle deliberately keeps WORK raised so
 * the next poll retries, mirroring _emperor_gc_want_collect. A disabled
 * runtime never raises it — explicit gc_collect() bypasses the gate. */
#define GC_GATE_WORK   1   /* a collection is wanted */
#define GC_GATE_TIMING 2   /* _gc_timing on: the slow path counts the poll */
#define GC_GATE_STRESS 4   /* EMPEROR_GC_STRESS_EVERY is live */
extern int _emperor_gc_poll_gate;
/* Non-static so the gate can be re-derived after the stress budget ends. */
extern unsigned _emperor_gc_stress_every;

/* The bits that are up for the whole run once init has parsed the env:
 * identical in every thread, so raisers may store them back verbatim without
 * a read-modify-write (and without the cache-line RMW traffic a fetch_or
 * would add on the allocation path). */
static inline int gc_poll_gate_static_bits(void) {
    return (_gc_timing ? GC_GATE_TIMING : 0)
         | (_emperor_gc_stress_every ? GC_GATE_STRESS : 0);
}

/* Raise a collection request: _emperor_gc_want_collect (declared in
 * emperor_gc.h — include it before this header) AND the gate, in that order,
 * so a poll that wins the race still finds the flag set. */
static inline void gc_request_collect(void) {
    if (_emperor_gc_disabled) return;
    __atomic_store_n(&_emperor_gc_want_collect, 1, __ATOMIC_RELAXED);
    __atomic_store_n(&_emperor_gc_poll_gate,
                     GC_GATE_WORK | gc_poll_gate_static_bits(),
                     __ATOMIC_RELAXED);
}

/* The allocator funnel's global lock. The span fast path is
 * per-arena and lock-free (gt_alloc_small); span retire/refill, the
 * malloc large path and the registries funnel through this. */
void gc_alloc_lock(void);
void gc_alloc_unlock(void);

/* Full greentea collect for the allocation slow path (conservative cover
 * on; re-entrancy and init guards live in the driver). */
void gc_collect_greentea_emergency(void);

/* Scan ONE object's body (ref-map walk, or the mapless conservative body
 * scan) with the mode-aware child-leaf: span children gray their slot
 * (gt_gray_object), malloc children mark+push the shared worklist. Used
 * by gc_mark_drain and the green-tea span/representative scans. */
void gc_scan_object_body(GCHeader* h);

/* Drain the shared large-object mark worklist (gc.c; the green-tea drain
 * interleaves it with the span queue). */
void gc_mark_drain(void);

/* ---- Per-thread allocation arenas (gc_span.c) ----
 * Each thread allocates small objects from ITS OWN arena: the per-class
 * current span plus the thread's fresh-object quarantine ring. The fast
 * path (gt_alloc_small hit on the current span) touches no shared state;
 * span retire/refill and superchunk acquisition take gc_alloc_lock. The
 * collector is still one stop-the-world pass over every arena — the
 * domain split (local collectors + shared-heap promotion at the handoff
 * points, design doc §10.2-10.6) is the next stage. */

typedef struct GtArena {
    void* current[GT_NCLASS];        /* GtSpan* per size class (opaque
                                      * here; the real type is gc_span.c's
                                      * internal GtSpan) */
    /* Fresh-object quarantine ring (moved from gc.c's global ring: the
                                      * cover must follow the allocating
                                      * THREAD — a boundary collect can
                                      * run while several arenas hold
                                      * in-flight temps). */
    void* quarantine[GC_QUARANTINE_RING];
    unsigned short quarantine_count;
    unsigned short quarantine_next;
    size_t alloc_bytes;              /* slot bytes allocated since the
                                      * last sweep (monotonic between
                                      * sweeps; folded into the global
                                      * heap-bytes sum) */
    /* Two-tier heap M2 (gc-two-tier-heap plan §2.9 (a)-1): allocations made
     * inside a Mutex.with critical section (crit_depth > 0) append to
     * crit_arr and are PROMOTED (shared bit set over their ref-map closure,
     * stopping at already-shared objects) when a critical section exits —
     * inside the lock (Inv-M2). The deferred-list shape (not alloc-time
     * stamping) is what makes construction-phase edges safe: the graph is
     * complete before promotion walks it. The array grows on demand and
     * stays allocated for reuse. */
    unsigned crit_depth;
    void** crit_arr;
    size_t crit_n;
    size_t crit_cap;
    /* Two-tier M4: DOMAIN-BOUND span pools (plan §4 M4 design note 1).
     * A span binds to the arena that classed it; retirement goes to THIS
     * arena's pools and refill prefers them — only fully-empty spans ever
     * return to the global pool. With Inv-S5 (unshared objects are never
     * referenced from outside their domain) this means a local collection
     * never touches another domain's spans. Opaque GtSpan* like current. */
    void* dom_partial[GT_NCLASS];
    void* dom_full[GT_NCLASS];
    /* Two-tier M4: local-collection budget tracking — spans refilled since
     * the last local collection, checked on the refill slow path against
     * the EMPEROR_GC_LOCAL quota (gc.c; 0 = off). */
    unsigned spans_since_local;
    int used;
} GtArena;

/* This thread's arena (assigns a registry slot on first use — call from
 * the allocating thread only). Never NULL once returned. */
GtArena* gt_arena_thread(void);

/* Fresh-object quarantine ring push into ARENA (gc.c's malloc large path
 * uses this with its own arena). */
void gt_quarantine_push(GtArena* a, void* user);

/* Snapshot helper for gc.c's quarantine marking: visit every registered
 * arena (stop-the-world or single-threaded moment). */
void gt_each_arena(void (*fn)(GtArena* a, void* ctx), void* ctx);


/* ---- gc_span.c API used by gc.c ---- */

/* Allocate SIZE bytes from the span heap (slot = header+SIZE rounded into
 * a size class). Returns NULL when SIZE exceeds GT_MAX_BODY (caller falls
 * back to the malloc path) or on allocation failure after an emergency
 * collect. Zeroes the whole slot payload; stamps the header. */
void* gt_alloc_small(int size, int is_string);

/* Resolve a candidate address to its owning span slot's user pointer
 * (interior or exact), or NULL. O(1): range test + 8KiB mask + magic. */
void* gt_slot_owner(const void* p);

/* Root/child marking entry: gray OBJ's span slot if span-resident
 * (returns 1 — the header must not be touched; colors are bitmap-side),
 * or return 0 for the malloc large path. */
int gt_gray_object(void* obj);

/* Combined resolve+gray for the mark leaves: one range lookup.
 * Returns 1 when CANDIDATE resolved to a live span slot (owner grayed),
 * 0 when not span-resident (caller falls back to the malloc index). */
int gt_mark_candidate(void* candidate);

/* Drain the green-tea mark work: the large worklist, the span FIFO queue
 * (word-differential `gray & ~black` scans), and the representative
 * single-object fast path. Call once after all roots are marked. */
void gt_drain(void);

/* GC_VERIFY: assert per-span gray==black and black ⊆ alloc (post-drain,
 * pre-sweep); aborts loudly on violation. */
void gt_verify_invariants(void);

/* Sweep: clear dead alloc bits, recompute live counts/bytes, and
 * repool spans (full/partial/empty). No user code runs here. */
void gt_sweep_reclaim(void);

/* Mark-failure recovery: clear every slot's marked bit (partial marking
 * retains everything — the next cycle re-judges from scratch). */
void gt_reset_marks(void);

/* ---- Unified heap goal ----
 * ONE budget for BOTH heaps: the collection trigger compares
 * malloc-allocated + span-allocated bytes against gc.c's unified goal,
 * and the driver's post-sweep update (gc_update_unified_goal) recalculates
 * it from the COMBINED live set. The previous split — a malloc-side
 * threshold sized to malloc-live alone plus a span-side goal sized to
 * span-live alone — let the smaller side's budget fire while the other
 * side held most of the live set, so every cheap-looking cycle still
 * marked O(total live) (bootstrap: 59 collections at a ~87MiB malloc-side
 * equilibrium while 250MiB lived in spans). gt_alloc_small AND the malloc
 * path in _emperor_gc_alloc call gc_unified_goal_reached(); the poll drains
 * the flag it raises.
 *
 * goal = max(min_heap, factor x combined_live) + young_budget — the
 * proportional base (2x-live threshold) plus one nursery budget of
 * guaranteed churn slack between collections. Live-heavy sweeps
 * (freed < live/4, the LSP recompile shape) still grow x4 capped at 4TiB —
 * each cycle costs O(live blocks), so collecting while freeing almost
 * nothing is pure overhead. Peak heap is bounded at (1+factor) x live +
 * young. */
void gc_update_unified_goal(size_t malloc_freed); /* post-sweep, driver */
int gc_unified_goal_reached(void);                /* allocation triggers */
uint64_t gc_unified_goal_value(void);             /* introspection (gt_stats 1) */
void gc_set_goal_params(size_t factor, size_t min_heap); /* env knobs (0 = keep) */

/* The young allocation budget (gc.c's EMPEROR_GC_YOUNG, default 128MiB):
 * the unified goal's additive churn slack (see gc_update_unified_goal). */
size_t gc_young_budget_cap(void);

/* Additive gc_info fields. which: 0 live slot bytes, 1 goal
 * bytes (the UNIFIED goal — gc_unified_goal_value), 2/3/4 full/partial/
 * empty span counts, 5 wholesale-recycled spans (cumulative), 6
 * representative fast-path scans (cumulative), 7 completed cycles,
 * 8 superchunks. */
uint64_t _emperor_gc_gt_stats(int which);

/* Allocated slot bytes (== live bytes right after a sweep). */
uint64_t gt_heap_bytes(void);

/* The exact figure (global + unflushed arena deltas) — user queries and
 * boundary predicates only, never the per-allocation hot path. */
uint64_t gt_heap_bytes_exact(void);

/* Live slot bytes and last cycle's freed slot bytes (unified-goal inputs). */
uint64_t gt_live_bytes(void);
uint64_t gt_last_freed(void);

/* Test-only: the size-class slot SIZE would be charged (0 = large path). */
unsigned gt_slot_size_for(int size);

/* ---- Two-tier heap M2: write-point promotion (plan §2.9/§2.10) ----
 * Master switch: EMPEROR_GC_REGION=0 (env) keeps today's exact single-heap
 * behavior — promotion and critical-section tracking become no-ops. */
extern int _emperor_gc_region;

/* Promote OBJ's not-yet-shared closure: set the shared bit on every
 * reachable object lacking it, stop at already-shared ones (Inv-S1's
 * monotonic closure makes stopping sound). OBJ is a user pointer; foreign
 * / non-GC values are ignored. Iterative (own worklist — deep graphs must
 * not recurse). Returns the number of objects newly promoted. */
size_t _emperor_gc_promote(void* obj);

/* Cumulative promotion count (tests / gc_gt_stats). */
size_t _emperor_gc_promoted_total(void);

/* Critical-section bracket around Mutex.with callbacks. The innermost (and
 * every) exit drains the arena's crit list: each tracked allocation is
 * promoted inside the lock (Inv-M2). */
void _emperor_gc_crit_enter(void);
void _emperor_gc_crit_exit(void);

/* gc_span.c: set the shared bit on a span-resident object and bump its
 * span's nshared. Returns 1 when USER resolved to a span slot (shared set
 * or already set), 0 when not span-resident (caller falls back to the
 * malloc header). */
int gt_promote_slot(void* user);

/* gc_span.c: append USER to the arena's critical-section allocation list
 * (allocation fast path, crit_depth > 0 only). */
void gt_crit_track(GtArena* a, void* user);

/* Two-tier M3 (GC_VERIFY, STW-time only): gc.c walks every shared object's
 * body and aborts on an edge to an UNSHARED GC object (Inv-S1/S2). */
void gc_verify_shared_edges(void);
void gt_verify_shared_objects(void (*fn)(GCHeader*));

/* ---- Two-tier M4a: domain-local collection (plan §2.5 / §4 M4) ----
 * Marks via header.marked (NOT the span bitmaps — no cross-collector
 * bitmap reuse), roots = the FULL root set with the leaf rule "unshared
 * non-owning-domain span objects are skipped" (equivalent to exact local
 * roots under Inv-S5), shared objects are opaque (Inv-S8), locally
 * reachable large objects are traversed (hole 2) via a marked-guard
 * worklist that is cleared before return. Sweeps ONLY this arena's
 * domain pools + current spans: alloc & ~marked & ~shared, then resets
 * marked on survivors. Mutually exclusive with the global collector
 * both directions (active-count spin / collecting check). */
int gc_collect_local(void);          /* returns 1 when a local cycle ran */
void gc_local_check(GtArena* a);     /* refill-path quota check (gc_span.c
                                        calls it; folds the spans count) */
size_t gt_local_sweep(GtArena* a);   /* gc_span.c: sweep this arena's spans;
                                        returns freed slot bytes; resets
                                        marked on live slots */
int gt_span_of_arena(GtArena* a, void* candidate, void** owner_out);

/* DIAGNOSTIC (EMPEROR_GC_LOCAL_AUDIT, gc.c): report own-domain slots the
 * full mark reaches but the local pass missed. */
void gt_audit_local_gap(GtArena* a);
/* Resolve candidate to its owning slot's user ptr ONLY when the span
 * belongs to arena A (the local-marking leaf helper). */

#endif
