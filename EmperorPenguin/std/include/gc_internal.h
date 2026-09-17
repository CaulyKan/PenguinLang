#ifndef EMPEROR_GC_INTERNAL_H
#define EMPEROR_GC_INTERNAL_H

/* Shared internals of the GC runtime's two translation units: gc.c (root
 * discovery, mark core, large-object malloc heap, collect drivers) and
 * gc_span.c (green-tea span heap, GC v3). NOT part of the public runtime
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
} GCHeader;

/* ---- Span-heap limits (layout constants shared by both units) ---- */

/* Largest slot served by a span; a 24B header + body over this takes the
 * malloc "large" path in _emperor_gc_alloc. */
#define GT_MAX_SLOT 512
#define GT_MAX_BODY ((int)(GT_MAX_SLOT - sizeof(GCHeader)))

/* Unified-goal defaults (spec §9.2): the floor (EMPEROR_GC_MIN_HEAP=0
 * keeps this) and the live multiplier (EMPEROR_GC_GOAL_FACTOR=0 keeps
 * this), applied to the COMBINED malloc+span live set. */
#define GC_GOAL_MIN_HEAP (4u * 1024 * 1024)
#define GC_GOAL_FACTOR 2

/* ---- gc.c state used by gc_span.c ---- */

extern int _gc_greentea;             /* EMPEROR_GC_MODE=greentea */
extern int _gc_timing;               /* stats/profile counters active */
extern size_t _gc_st_allocs, _gc_st_alloc_bytes;
extern int _emperor_gc_disabled;
extern int _emperor_gc_mark_failed;  /* set by either unit on mark OOM */
extern size_t _emperor_gc_mark_stack_top; /* shared worklist depth (gt_drain) */

/* Run a dead object's dispose_mem, if any (gc.c's finalizer probe). */
void _emperor_gc_finalize(GCHeader* h);

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

/* Combined resolve+gray for the mark leaves (spec §7.1): one range lookup.
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

/* Sweep phase 1: run finalizers on every dead (alloc & ~black) slot. No
 * state change — dead bodies stay intact for the malloc sweep's own
 * finalizer pass, which runs between this and gt_sweep_reclaim. */
void gt_sweep_finalize(void);

/* Sweep phase 2: clear dead alloc bits, recompute live counts/bytes, and
 * repool spans (full/partial/empty). No user code runs here. */
void gt_sweep_reclaim(void);

/* Mark-failure recovery: clear every slot's marked bit (partial marking
 * retains everything — the next cycle re-judges from scratch). */
void gt_reset_marks(void);

/* ---- Unified heap goal (GC v3.1, spec §9.2) ----
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
 * the flag it raises. Conservative mode (no span heap) keeps the legacy
 * malloc-only threshold in gc.c.
 *
 * goal = max(min_heap, factor x combined_live) + young_budget — v2's
 * economics translated to one heap: the proportional base (old-gen 2x-live
 * threshold) plus one nursery budget of guaranteed churn slack between
 * collections (the amortization the M3 garbage-rich doubling used to
 * synthesize; measured as a cap-on-doubling it either never fired during
 * live ramps or overshot peak heap to ~4x live, so the slack is baked into
 * the base instead). Live-heavy sweeps (freed < live/4, the LSP recompile
 * shape) still grow x4 capped at 4TiB — each cycle costs O(live blocks),
 * so collecting while freeing almost nothing is pure overhead. Peak heap
 * is bounded at (1+factor) x live + young. */
void gc_update_unified_goal(size_t malloc_freed); /* post-sweep, driver */
int gc_unified_goal_reached(void);                /* allocation triggers */
uint64_t gc_unified_goal_value(void);             /* introspection (gt_stats 1) */
void gc_set_goal_params(size_t factor, size_t min_heap); /* env knobs (0 = keep) */

/* The young allocation budget (gc.c's EMPEROR_GC_YOUNG, default 128MiB):
 * the unified goal's additive churn slack (see gc_update_unified_goal). */
size_t gc_young_budget_cap(void);

/* Additive gc_info fields (spec §12). which: 0 live slot bytes, 1 goal
 * bytes (the UNIFIED goal — gc_unified_goal_value), 2/3/4 full/partial/
 * empty span counts, 5 wholesale-recycled spans (cumulative), 6
 * representative fast-path scans (cumulative), 7 completed cycles,
 * 8 superchunks. */
uint64_t _emperor_gc_gt_stats(int which);

/* Allocated slot bytes (== live bytes right after a sweep). */
uint64_t gt_heap_bytes(void);

/* Live slot bytes and last cycle's freed slot bytes (unified-goal inputs). */
uint64_t gt_live_bytes(void);
uint64_t gt_last_freed(void);

/* Test-only: the size-class slot SIZE would be charged (0 = large path). */
unsigned gt_slot_size_for(int size);

#endif
