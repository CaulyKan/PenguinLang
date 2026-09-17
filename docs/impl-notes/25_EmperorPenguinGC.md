# EmperorPenguin GC v3 — Green Tea Span Collector

> **Status: implemented (M0–M4 complete, branch `feature/gc-greentea`).**
> The span heap (`EmperorPenguin/std/c/gc_span.c` + the greentea driver in
> `gc.c`) is the default collector; the v2 generational machine was deleted
> at M4b (~1550 lines) and write-barrier emission retired at M4c (ABI tag
> `emperor-rt-gc3-gt1`). Sections marked with historical `gc.c:NNN` anchors
> refer to the pre-v3 tree and are kept as design rationale.
> Implementation plan and milestones: `.agents/plans/gc-greentea-span-heap.md`.

Green Tea is Go's rework of **small-object heap marking** (golang/go#73581,
[`go.dev/blog/greenteagc`](https://go.dev/blog/greenteagc); experiment in Go
1.25, default in Go 1.26): instead of queuing individual object pointers, the
marker queues **whole 8 KiB spans** of one size class and scans them in
address order, so pointer chasing turns into linear, prefetch-friendly bitmap
passes. Go reports 10–40% lower GC overhead on GC-heavy programs. It is *not*
a stack-scanning redesign — EmperorPenguin's precise root machinery (frame
chains, ref-maps, tracked buffers) already exists and is **kept unchanged**.

## 1. Scope

| | |
| --- | --- |
| **Replaces** | The GC v2 *heap organization and mark/sweep core*: copying nursery (evacuate/promote/pin/tombstone, `gc_minor` gc.c:2415-2769), malloc old generation as the *only* object path, per-object mark worklist (gc.c:1170-1189), card-table write barriers (gc.c:898-925). |
| **Keeps unchanged** | Root discovery: precise frame chains (`EmperorGcFrame`, gc.c:583-594; LLVMEmitter.penguin:1642-1667), global root registry (gc.c:294-322), tracked buffer regions (`_emperor_gc_track_buffer`), meta pins (gc.c:1085-1109), quarantine ring (gc.c:638-650), conservative stack cover. Per-type **ref-map programs** and `gc_refmap_walk` (gc.c:1605-1713). Safepoint polls `_emperor_gc_poll` before every call/alloc (LLVMEmitter.penguin:4475-4482). The emitted-code symbol ABI (`_emperor_alloc_impl`, `_emperor_gc_frame_head`, barrier symbols — see §11). |
| **Gains** | Single-generation, non-moving, allocation-locality-friendly span heap; batched span marking; O(per-span) wholesale reclamation of dead spans; no write barriers (STW, single mutator); deletion of the entire nursery/pin machine at M4. |
| **Does not take from Go** | Concurrent marking and scheduler-run-queue work distribution (the runtime is single-mutator: user-level coroutines switch stacks on one OS thread, scheduler.c); headerless slots (our objects keep their 24 B `GCHeader`, §4.4); SIMD bitmap kernels (future work, §16). |

## 2. Background

### 2.1 GC v2 today (what is being replaced)

| Mechanism | Where | Note |
| --- | --- | --- |
| Nursery: 64 KiB bump chunks cut from 1 MiB superchunks, 128 MiB young budget | gc.c:695-721, 2250-2314 | Copying: survivors promoted to malloc old gen via tombstones (`gc_promote_young` gc.c:1511-1540); objects seen only by conservative words **pinned**, chunk demoted |
| Old gen: `malloc` blocks on `_emperor_gc_allocation_list` + sorted start-address index for interior-pointer resolution | gc.c:70, 210-268 | Mark/sweep per cycle; adaptive threshold 2× live (gc.c:3303-3311) |
| Marking: iterative per-object worklist (`GCHeader**` stack) | gc.c:1170-1189 | Every reachable object pushed/popped individually — the hot loop Green Tea targets |
| Write barrier: 512 B card table (open-addressed key set), marked on every pointer store into old objects | gc.c:898-925, 3474-3494 | Unconditional in emitted code (LLVMEmitter.penguin:4289-4318) |
| Modes: `EMPEROR_GC_MODE` = `precise` (default, generational) / `legacy` / `conservative`; `EMPEROR_GC_NOGEN`, stress knobs | gc.c:3366-3414 | The `greentea` mode (§12) slots in here |
| Safety nets: conservative main-stack cover on every major (gc.c:2836-2844), quarantine ring of 512 recent allocations, `GC_VERIFY` differential machinery | gc.c:3094-3265 | All retained in v3 |

The heap itself is already scanned **precisely** via per-type ref-map programs
(`@<T>_refmap` constants, encoding in `emperor_types.h:16-43`; writer
`refmap_append_*` LLVMEmitter.penguin:2737-2844). v3 changes *where objects
live* and *how marking is scheduled* — not what is considered a reference.

### 2.2 Green Tea in Go (what is being ported)

- Work items are **spans**: 8 KiB-aligned regions holding objects of exactly
  one size class; the experiment covers objects ≤ 512 B (Go 1.25/1.26).
- Each object gets a **gray bit and a black bit** in its span (white = neither).
  On dequeue, the scanner computes `gray & ~black` per bitmap word, copies the
  result to black, and scans exactly those objects. Children mark gray bits in
  *their* spans.
- A per-span **`enqueued` flag** dedups queue insertion (a span is queued at
  most once while pending).
- A **representative + hit flag** short-circuits spans where exactly one
  object was marked: the lone object is scanned directly, skipping span
  bookkeeping. (Go's data: in real heaps many spans have exactly one live
  object per cycle.)
- Queue is FIFO, so spans are scanned in roughly ascending address order —
  that is where the cache-miss reduction (~50% fewer L1/L2 misses reported)
  comes from.
- Large objects keep the old per-object path.

## 3. Design overview

Single generation, non-moving, stop-the-world (the world is one OS thread;
collection runs inside `_emperor_gc_poll` on the mutator, coroutine stacks are
parked by construction and enumerated via their frame chains).

```
                    emitted .ll  (UNCHANGED)
   poll @ every call/alloc ──┐   frame-chain link/unlink
   barrier calls (→ no-op)   │   refmaps, metadata globals
                             ▼
 ┌──────────────────────────────────────────────────────────┐
 │  gc.c runtime, greentea mode                             │
 │                                                          │
 │  roots (UNCHANGED set)          gt core (NEW, gc_span.c) │
 │  ┌─────────────────────┐        ┌──────────────────────┐ │
 │  │ frame chains (all    │──mark──▶ GtWorkQueue (FIFO)  │ │
 │  │ stacks incl. corout.)│        │  + rep fast-path     │ │
 │  │ global root registry │        │  + large-object      │ │
 │  │ tracked buffers      │        │    mark stack        │ │
 │  │ meta pins            │        └──────────┬───────────┘ │
 │  │ quarantine ring      │                   │ drain:      │
 │  │ conservative cover   │                   │ gray^black  │
 │  └─────────────────────┘                   │ word-diff   │
 │                                            ▼             │
 │  heap: spans ≤512B ──────────────► sweep (wholesale /     │
 │        malloc blocks >512B ──────►   per-object + large)  │
 └──────────────────────────────────────────────────────────┘
```

Tricolor encoding without object-header writes:

- **white** = `{gray:0, black:0}` — unreachable (this cycle) unless proven,
- **gray** = `{gray:1, black:0}` — reachable, fields not yet scanned,
- **black** = `{gray:1, black:1}` — reachable, fields scanned.

A slot's bits live in its **span bitmap**, so setting/testing a color is a
shift+or on a 64-bit word shared by 64 neighboring objects — no per-object
header traffic during marking. The per-object `GCHeader.marked` stays in use
only for the **large-object** (malloc) path.

**Why no write barrier is sound:** marking and sweeping both complete inside
one `gt_collect()` call on the only thread; the mutator never runs while
objects are gray, so no old→new/white-to-black violation can occur. Barrier
entry points become no-ops (§11) — already-emitted `.ll` keeps working
unchanged, and v3 does not require re-emitting anything.

## 4. Memory layout

### 4.1 Superchunk → spans

```c
#define GT_SPAN_SIZE     8192              /* one span, 8 KiB-aligned */
#define GT_SPAN_HDR      128               /* sizeof(GtSpan), fixed  */
#define GT_BITMAP_WORDS  4                 /* 4*64 = 256 slots max   */
#define GT_SUPER_SPANS   128               /* 128 * 8 KiB = 1 MiB payload */
#define GT_SUPER_SIZE    (GT_SPAN_SIZE * (GT_SUPER_SPANS + 1))
```

A superchunk is `aligned_alloc(GT_SPAN_SIZE, GT_SUPER_SIZE)` (C11; Win32:
`_aligned_malloc`; fallback: over-allocate + align internally, keep the raw
base for free). The **first 8 KiB** holds the `GtSuper` bookkeeping block so
that the remaining 128 spans land exactly on 8 KiB boundaries.

```
superchunk (GT_SUPER_SIZE = 8192*129 bytes, 8 KiB-aligned)
┌────────────┬────────────┬────────────┬────────────┬─────┐
│ GtSuper    │  span #0   │  span #1   │  span #2   │ ... │  (128 spans)
│ 8 KiB      │  8 KiB     │  8 KiB     │  8 KiB     │     │
└────────────┴────────────┴────────────┴────────────┴─────┘
                  │
                  ▼ span #k (one size class)
┌───────────────────────────────┬────────┬────────┬────────┬─────┬────────┐
│ GtSpan header (128 B)         │ slot 0 │ slot 1 │ slot 2 │ ... │ slot N │
│ class,nslots,bitmaps,flags... │ 32..512 B each, 16-byte aligned        │
└───────────────────────────────┴────────┴────────┴────────┴─────┴────────┘
```

### 4.2 Size classes

Objects whose **slot size** (24 B `GCHeader` + body, rounded to the class) is
≤ 512 B allocate from spans. Class spacing follows Go's table up to 512 B
(minus classes below our 32 B minimum header+word slot):

```
slot sizes: 32 48 64 80 96 112 128 144 160 176 192 208 224 240 256
            288 320 352 384 416 448 480 512          (23 classes)
```

| class | slot | body capacity | slots/span (8064 B usable) |
| --- | --- | --- | --- |
| 0 | 32 | 8 | 252 |
| 1 | 48 | 24 | 168 |
| 2 | 64 | 40 | 126 |
| … | … | … | … |
| 14 | 256 | 232 | 31 |
| … | … | … | … |
| 22 | 512 | 488 | 15 |

Larger requests (slot > 512 B) take the existing malloc-block path unchanged
(`_emperor_gc_allocation_list`, sorted index, per-object mark — the "large"
path throughout this document). Strings allocate through the same router
(`_emperor_string_alloc` → small path when the slot fits a class).

### 4.3 Address → span (O(1), no index)

Spans are 8KiB-aligned by construction, so resolution is a mask + validation:

```c
static GtSpan* gt_span_of(const void* p) {
    if (!gt_in_super_range(p)) return NULL;            /* EXACT ranges    */
    GtSpan* s = (GtSpan*)((uintptr_t)p & ~(uintptr_t)(GT_SPAN_SIZE - 1));
    if (s->magic != GT_SPAN_MAGIC) return NULL;       /* false-hit guard   */
    return s;
}
```

`gt_in_super_range` binary-searches the sorted **exact super ranges**
(kept in gc_span.c, mirroring v2's `_gc_super_ranges`): the loose
`[heap_lo, heap_hi)` span covers unmapped gaps between the supers' mmaps,
and `gt_span_of` **dereferences** the masked base — a conservative-scan
candidate landing in a gap would SIGSEGV on the magic read (found in M1
testing: ~13% of finstorm runs under ASLR). Membership in an actual super
makes the deref safe by construction (supers are never freed; the masked
base of an in-super candidate lies in the same mapped super). The magic
check stays for the super's bookkeeping block (a distinct `GT_SUPER_MAGIC`
at offset 0 fails it). This replaces the sorted malloc index for small
objects; the index survives for large objects only.

### 4.4 Object layout (unchanged)

Every slot still begins with the 24 B `GCHeader`
(`{next, marked, is_string, size}`, gc.c:63-68) followed by the body whose
word 0 is the `EmperorClassMetadata*` stamped by codegen (or
`_emperor_string_metadata*` + length + data for strings,
`emperor_string.h:27-35`). Consequences:

- ref-map walking, `dispose_mem` finalizers, `is_string` opacity, and the
  emission contract all work untouched;
- `next`/`marked` are redundant in spans (pool links and colors are span-level)
  — accepted overhead; headerless slots are future work (§16).

## 5. Data structures

New translation unit `EmperorPenguin/std/c/gc_span.c` (span heap machine:
size classes, superchunk supply, allocation, O(1) resolve, sweep/finalizer
passes); the **collection driver lives in gc.c** (`gc_collect_greentea`,
beside its siblings — it shares the timing stats and root-walk statics).
The shared interface between the two units is
`EmperorPenguin/std/include/gc_internal.h` (GCHeader, GT layout constants,
gt_* API, the few exported gc.c helpers); `gc_span.c` joins libcore_builtin.a
via the std/c Makefile SRC list.

```c
/* ---- span ---- */
#define GT_F_ENQUEUED    0x01   /* span sits in GtWorkQueue.q            */
#define GT_F_REP_PENDING 0x02   /* rep_slot grayed, not yet scanned      */
#define GT_F_REP_HIT     0x04   /* ≥2 distinct objects grayed → real scan */

typedef struct GtSpan {
    uint32_t magic;              /* GT_SPAN_MAGIC, guards gt_span_of()   */
    uint8_t  size_class;         /* index into gt_classes[]              */
    uint8_t  flags;              /* GT_F_*                               */
    uint16_t nslots;             /* slots per span for this class        */
    uint16_t free_index;         /* next slot probe hint (alloc)         */
    uint16_t live_count;         /* allocated (not yet swept-dead) slots */
    uint16_t rep_slot;           /* representative slot index            */
    uint64_t alloc_bits[GT_BITMAP_WORDS];  /* slot in use                */
    uint64_t gray_bits[GT_BITMAP_WORDS];   /* reachable, unscanned       */
    uint64_t black_bits[GT_BITMAP_WORDS];  /* reachable, scanned         */
    struct GtSpan* next;         /* pool list link (partial/full/empty)  */
    struct GtSuper* super;       /* owning superchunk                    */
} GtSpan;                        /* == GT_SPAN_HDR (128 B), static_assert */

/* ---- size class ---- */
typedef struct GtSizeClass {
    uint16_t slot_size;          /* 32..512, multiples of 16             */
    uint16_t nslots;             /* floor(8064 / slot_size)              */
} GtSizeClass;                   /* gt_classes[23], const                */

/* ---- superchunk bookkeeping (first 8 KiB of a superchunk) ---- */
typedef struct GtSuper {
    struct GtSuper* next;        /* _gt_supers list (never freed by default) */
    void*  raw;                  /* aligned_alloc base (free target)     */
    size_t size;
    uint32_t nspans;             /* GT_SUPER_SPANS                       */
    uint32_t nempty;             /* spans still on the empty pool        */
} GtSuper;

/* ---- heap ---- */
typedef struct GtHeap {
    GtSpan* current[GT_NCLASS];  /* active allocation span per class     */
    GtSpan* partial[GT_NCLASS];  /* swept spans with free slots          */
    GtSpan* full[GT_NCLASS];     /* swept spans, no free slots           */
    GtSpan* empty;               /* class-agnostic; reclassed on demand  */
    GtSuper* supers;
    /* stats */
    size_t heap_bytes;           /* sum of allocated slot bytes + large  */
    size_t live_bytes;           /* after last sweep                     */
    /* goal_bytes moved to gc.c at v3.1: ONE unified dual-heap goal    */
    uint32_t n_wholesale;        /* spans recycled whole this cycle      */
    uint32_t n_rep_scans;        /* representative fast-path hits        */
    int     mark_overflow;       /* queue/stack alloc failure → retain   */
} GtHeap;

/* ---- mark work queue (FIFO ring of spans) ---- */
typedef struct GtWorkQueue {
    GtSpan** q; size_t cap, head, count;
    /* plus the rep fast-path list and the large-object stack: */
    GtSpan*  rep_list;           /* singly-linked via ->next              */
    GCHeader** large;            /* per-object stack, malloc'd blocks     */
    size_t large_cap, large_top;
} GtWorkQueue;
```

Notes:

- `GtSpan.next` is overloaded: pool link while parked, `rep_list` link while a
  pending representative (a span is never in two structures at once — the
  flags say which).
- The empty pool is **class-agnostic**: an emptied span is re-`memset` and
  re-classed on demand, so classes cannot strand memory.
- All structures are process-global singletons; no locking anywhere (single
  mutator, same invariant as v2 — gc.c has no locks today).

## 6. Allocation

### 6.1 Routing

`_emperor_alloc_impl(int size)` (emitted at every `new`/`BOX`,
LLVMEmitter.penguin:4934/5123/5489/5507; strings via
`_emperor_string_alloc`):

```
slot = round8(24 + size)
if (greentea mode && slot <= 512)  return gt_alloc_small(size, slot);
else                               return <existing malloc-block path>;
```

### 6.2 Fast path

```c
static void* gt_alloc_small(int size, int slot) {
    unsigned cls = gt_class_of[slot];              /* O(1) lookup table   */
    GtSpan* s = _gt.current[cls];
    if (!s && !(s = gt_refill_current(cls)))       /* slow path, §6.3     */
        return NULL;
    for (unsigned w = s->free_index >> 6; w < GT_BITMAP_WORDS; w++) {
        uint64_t free = ~s->alloc_bits[w];
        if (!free) continue;
        unsigned idx = (w << 6) + ctz64(free);
        s->alloc_bits[w] |= 1ull << (idx & 63);
        s->free_index = idx + 1;
        s->live_count++;
        _gt.heap_bytes += gt_classes[cls].slot_size;
        GCHeader* h = gt_slot_header(s, idx);
        h->next = NULL; h->marked = 0; h->is_string = 0; h->size = size;
        memset(h + 1, 0, size);                    /* zeroing stays       */
        if (_gt.heap_bytes > _gt.goal_bytes) _gt_want_collect = 1;
        return h + 1;
    }
    /* current span is full: retire and retry once */
    gt_pool_push(&_gt.full[cls], s);
    _gt.current[cls] = NULL;
    return gt_alloc_small(size, slot);
}
```

Cost: one bitmap scan from a hint + one `memset`. Same order as the v2
nursery bump (which also memsets), minus the per-object list append.

### 6.3 Slow path (refill)

```
gt_refill_current(cls):
    1. partial[cls] non-empty            → pop, make current.
    2. else empty pool non-empty         → memset header, set class/nslots,
                                            make current (reclass).
    3. else cut spans from a fresh superchunk (gt_super_acquire);
       push the remaining 127 onto the empty pool.
    4. if superchunk alloc fails:
         gt_collect(emergency=1);        /* free memory, then retry      */
         retry steps 1-3 once; on failure return NULL (OOM abort path,
         same fatal reporting as v2).
```

Slots freed by a sweep are only reusable **after** that sweep (allocation
never clears alloc bits; only sweep does). No mid-cycle reuse — identical
lifetime semantics to v2, and it is what makes whole-dead spans possible.

## 7. Marking

### 7.1 Entry point

All root sources (§10) funnel into one visitor, `gt_mark(void* p)`, where `p`
is an exact user pointer from precise roots or an arbitrary candidate from
the conservative cover:

```c
static void gt_mark(void* p) {
    GtSpan* s = gt_span_of(p);
    if (s) {
        void* base = gt_slot_user_base(s, p);   /* interior → owner slot  */
        if (base) gt_gray_slot(s, gt_slot_index(s, base));
        return;
    }
    GCHeader* h = gc_resolve_block(p);          /* large path, gc.c:254   */
    if (h) gt_mark_large(h);
}
```

`gt_slot_user_base` resolves an interior pointer to its owning slot in O(1):
`idx = (p − span_payload)/slot_size`, verify the alloc bit and that `p` lies
within `[slot_user_base, slot_user_base + h->size)` (same containment rule as
v2's `gc_owner_in_chunk`, gc.c:1240-1269 — enum payloads at +16 and nested
value classes are interior by design). This replaces chunk walking with an
address division.

### 7.2 Graying a slot (dedup + representative bookkeeping)

```c
static void gt_gray_slot(GtSpan* s, unsigned idx) {
    uint64_t bit = 1ull << (idx & 63);
    unsigned w   = idx >> 6;
    if (s->gray_bits[w] & bit) return;          /* already gray           */
    s->gray_bits[w] |= bit;
    if (s->flags & GT_F_ENQUEUED) return;       /* queued: drain will see */
    if (s->flags & GT_F_REP_PENDING) {
        if (s->rep_slot == idx) return;
        s->flags |= GT_F_REP_HIT;               /* 2nd distinct object    */
        gt_enqueue(s);                          /* real scan required     */
        return;
    }
    s->rep_slot  = idx;                         /* candidate lone object  */
    s->flags    = (s->flags & ~GT_F_REP_HIT) | GT_F_REP_PENDING;
    gt_rep_push(s);                             /* NOT enqueued           */
}
```

A span with exactly one gray object never enters the work queue: it sits on
the **representative ARRAY** and its lone object is scanned directly at
drain time (`n_rep_scans` counts the hits). A second distinct object flips
`REP_HIT` and enqueues it for a full bitmap scan.

**Implementation deviation from the original sketch (M2):** the
representative bookkeeping is a growable array (consumed by head index),
not the spec's `rep_list` linked through `GtSpan.next` — during marking a
span's `next` is its POOL link (current/partial/full), so it cannot double
as a mark-structure link. A re-grayed candidate after the span left both
structures clears the stale `REP_HIT` before re-pending (else the drain
would skip it as already promoted). The mark leaves do not
resolve-then-gray: `gt_mark_candidate(p)` merges the span membership test,
owner resolution and the gray into ONE range lookup (the paired form paid
the super-range binary search twice per reference word — 52% of
deepsurvive's mark in the first M2 cut).

### 7.3 Drain loop

```c
static void gt_drain(void) {
    for (;;) {
        GCHeader* lh;
        if (gt_large_pop(&lh)) { gt_scan_object(lh + 1); continue; }

        GtSpan* s = gt_dequeue();               /* clears GT_F_ENQUEUED   */
        if (s) { gt_scan_span(s); continue; }

        s = gt_rep_list_pop();
        if (!s) break;                          /* all work done          */
        assert(!(s->flags & GT_F_REP_HIT));     /* HIT path enqueued it   */
        s->flags &= ~GT_F_REP_PENDING;
        unsigned idx = s->rep_slot;
        s->black_bits[idx >> 6] |= 1ull << (idx & 63);
        _gt.n_rep_scans++;
        gt_scan_object(gt_slot_user(s, idx));   /* single-object fast path */
    }
}

static void gt_scan_span(GtSpan* s) {           /* full bitmap scan       */
    s->flags &= ~GT_F_REP_PENDING;              /* (if HIT promoted it)   */
    for (;;) {
        int progressed = 0;
        for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
            uint64_t fresh = s->gray_bits[w] & ~s->black_bits[w];
            if (!fresh) continue;
            s->black_bits[w] |= fresh;          /* blacken batch          */
            while (fresh) {
                unsigned idx = (w << 6) + ctz64(fresh);
                fresh &= fresh - 1;
                gt_scan_object(gt_slot_user(s, idx));
            }
            progressed = 1;
        }
        if (!progressed) break;                 /* gray == black for s    */
    }
}
```

`gt_scan_object(user)` is the existing precise body walk, factored out of
v2's mark path: strings (`is_string`) return immediately; objects with a
ref-map call `gc_refmap_walk(map, MARK mode)` whose leaf action is now
`gt_mark(child)` instead of `_emperor_gc_mark_object`; mapless objects (foreign
metadata) fall back to the conservative body-word scan (gc.c:1761-1782).
Large objects use the per-object `marked` header bit and the `large` stack
(the old worklist, now only for malloc'd blocks).

**Termination.** A span leaves the queue only when `gray == black` locally
(the inner `progressed` loop); a span leaves `rep_list` only via HIT (which
re-enqueues it) or by scanning its representative (blackening it). Since the
mutator is stopped, no new gray bits can appear after drain structures are
empty — collection ends with `gray == black` globally, asserted under
`GC_VERIFY`.

**Failure.** If a queue/stack growth `realloc` fails mid-mark, marking is
partial: v3 keeps v2's retain-everything policy (`_emperor_gc_mark_failed`,
gc.c:1174-1177) — skip sweep, treat all objects live, retry next cycle.

### 7.4 Why this is faster than the per-object worklist

The v2 loop pays one worklist push/pop **per reachable object** and jumps
between objects in allocation-order-of-discovery (pointer-chasing locality).
The v3 loop pays one push/pop **per span with ≥2 marked objects**, walks
`gray & ~black` 64 slots at a time, and — because spans come out of the FIFO
in roughly the order they were first touched — touches object memory in
near-linear address order. Wholly-dead spans are never touched at all by the
marker. This is the same mechanism behind Go's reported 10–40% (their number
includes parallel scanning; our single-threaded expectation is the low end,
gated at ≥15% in benchmarks, §14).

## 8. Sweep

After a successful mark, `black` = live. Sweep walks each span once:

```c
static void gt_sweep_span(GtSpan* s) {
    /* black == live at this point; gray bits are all blackened copies   */
    unsigned live = 0;
    for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
        uint64_t alloc  = s->alloc_bits[w];
        uint64_t dead   = alloc & ~s->black_bits[w];
        if (dead) {
            uint64_t d = dead;
            while (d) {                          /* finalizers still run  */
                unsigned idx = (w << 6) + ctz64(d);
                d &= d - 1;
                gt_run_finalizer(gt_slot_header(s, idx));  /* §8.1        */
            }
            s->alloc_bits[w] = alloc & s->black_bits[w];
        }
        s->gray_bits[w] = 0;
        s->black_bits[w] = 0;
        live += popcount64(s->alloc_bits[w]);
    }
    s->live_count = live;
    s->free_index = 0;
    s->flags &= ~(GT_F_ENQUEUED | GT_F_REP_PENDING | GT_F_REP_HIT);
    _gt.live_bytes += live * gt_classes[s->size_class].slot_size;
    gt_repool(s, live == 0 ? EMPTY : live < s->nslots ? PARTIAL : FULL);
}
```

- **Wholesale reclamation**: `live == 0` spans skip per-slot free handling
  entirely — after the finalizer probe they go to the class-agnostic empty
  pool with a `memset` of the header. Infant-mortality workloads (the ones
  the v2 nursery served) mostly produce dead spans, so reclamation cost drops
  from O(objects) to O(spans).
- **Finalizer probe** (§8.1) is the only per-object work a dead span pays.
- Large-object sweep reuses v2's `_emperor_gc_sweep` (gc.c:1852-1912) minus
  the card-table compaction step.
- Supers are retained for the process lifetime by default (v2 behavior,
  gc.c:2264-2305 comment); releasing fully-empty supers beyond a cache
  threshold is a policy knob (§12), off by default.

### 8.1 Finalizers on dead slots

`dispose_mem` (`IMemoryDispose`) is load-bearing for raw-buffer owners
(vector/hashmap/array). At alloc time the runtime does not yet know whether
the caller's class has a destructor (body word 0 is stamped *after*
`_emperor_alloc_impl` returns), so `finalizer_count` cannot be maintained at
allocation. Instead, sweep probes each **dead** slot once: read body word 0 →
`metadata->destructor`; if non-NULL, call `dispose_mem(obj)` (same rules as
v2, gc.c:1835-1850). The probe is one sequential read per dead object
(prefetch-friendly); it is skipped for `is_string` slots. Optimization
deferred to future work: an additive `_emperor_alloc_impl(size, flags)` with a
compiler-known "no finalizer" bit (§16).

## 9. Collection cycle and triggers

### 9.1 `gt_collect`

```
gt_collect(emergency):
    _emperor_gc_collecting = 1
    stats reset (n_wholesale, n_rep_scans, live_bytes = 0)
    walk all roots → gt_mark            /* §10: registry, frame chains of
                                           every stack incl. parked coroutines
                                           (scheduler frame-head enumeration,
                                           scheduler.c:180), tracked buffers,
                                           meta pins, quarantine ring,
                                           conservative stack cover          */
    gt_drain()
    if (_gt.mark_overflow) { retain everything; goto done; }
    GC_VERIFY: assert gray==black per span; black ⊆ alloc;
               optional differential vs conservative whole-heap scan
    gt_sweep_all_spans()                /* §8                                */
    sweep_large()                       /* existing malloc-block sweep      */
    gc_update_unified_goal(freed)       /* §9.2 — ONE goal over both heaps  */
done:
    _emperor_gc_collecting = 0
```

Coroutine safety: identical to v2 majors — collection runs only inside a
safepoint poll on the running thread; all other stacks are parked, their
frame chains are complete and enumerable, and their scan regions are narrowed
to `[parked_sp, top]` (`_emperor_gc_scan_set_live`, scheduler.c:319-355).

### 9.2 Triggers and heap goal — the UNIFIED dual-heap budget

> **Erratum (v3.1)**: the original text here specified TWO side-local
> budgets — a span goal in gc_span.c (`max(4 MiB, factor × span-live)`) and
> the malloc-side adaptive threshold (`2 × malloc-live`, gc.c) — each
> enriched garbage-rich independently. Measured on the bootstrap this was
> the dominant cost bug of v3.0: each side sized itself to its OWN live
> set, but every cycle marks BOTH heaps at O(total live), so the smaller
> budget dictated the cadence while the cycle paid the big side's cost
> (malloc-live ~40 MiB vs span-live ~250 MiB → the malloc threshold
> collected every ~87 MiB, 59 fulls, each walking the 290 MiB combined
> live set; v2 did 10 majors on the same load). v3.1 replaces both with
> ONE goal over the COMBINED heap.

- `_emperor_gc_poll()` (emitted before every call and allocation) checks
  `_emperor_gc_want_collect` and runs the greentea cycle. This is the only
  regular trigger; the poll site contract is untouched.
- **Trigger (either allocation side)**: `_emperor_gc_total_allocated +
  gt_heap_bytes() >= unified_goal` → `_emperor_gc_want_collect = 1`.
  `gt_alloc_small` and the malloc path in `_emperor_gc_alloc` both call
  `gc_unified_goal_reached()` (gc.c; conservative mode keeps the legacy
  malloc-only threshold — it has no span heap and degenerates to the old
  behavior).
- **Update (once per cycle, post-sweep)**:
  `gc_update_unified_goal(malloc_freed)` in gc.c computes
  `live = malloc-live + gt_live_bytes()` and sets
  - `goal = max(GC_GOAL_MIN_HEAP 4 MiB, EMPEROR_GC_GOAL_FACTOR × live) +
    EMPEROR_GC_YOUNG (128 MiB)` — v2's economics on one heap: the
    proportional base is v2's old-gen 2×-live threshold, the additive
    young budget is the nursery's fixed churn amortization (the role the
    M3 garbage-rich doubling used to play; measured as a doubling-with-cap
    it either never fired during live ramps — a pure factor×live goal pins
    the garbage fraction at 1/(1+factor) — or overshot peak heap to ~4×
    live on ramp-then-churn profiles, so the slack is baked into the base);
  - live-heavy (`freed < live/4`): `goal ×= 4`, capped at 4 TiB — the v2
    old-gen escalation, kept for the LSP-recompile shape (freeing almost
    nothing while each cycle costs O(live blocks));
  - never shrinks (v2 parity).
  Peak heap is bounded at `(1+factor) × live + young` (measured bootstrap:
  goal 754 MiB + ~475 MiB fixed non-GC overhead = the 1231 MiB maxrss).
- `gt_update_goal`/`gt_set_goal_params` were deleted; the knob parsing
  (`EMPEROR_GC_GOAL_FACTOR`/`EMPEROR_GC_MIN_HEAP`) feeds
  `gc_set_goal_params` in gc.c, and `gc_gt_stats(1)` (goal bytes) reports
  the unified goal via `gc_unified_goal_value()`.
- **Emergency**: `gt_refill_current` failure (no free slot, empty pool dry,
  superchunk malloc failure) → inline emergency cycle, retry once, then OOM
  (v2's fatal path); the malloc path still collects inline at 8× the goal
  (C-heavy stretch with no poll in sight).
- `EMPEROR_GC_STRESS_EVERY=N` (gc.c) keeps working: collect every
  N allocations — the primary correctness hammer for §14.

## 10. Roots and safety nets (unchanged inventory)

| Root source | Mechanism | v3 change |
| --- | --- | --- |
| Stack frames (all stacks) | `EmperorGcFrame` chains linked to `_emperor_gc_frame_head` at entry, unlinked at ret (LLVMEmitter.penguin:1642-1667); bare slots + ref-map'd struct homes | none — leaves call `gt_mark` |
| Globals | `_emperor_gc_add_root(void**)` registry; value-class globals as scan regions (LLVMEmitter.penguin:1695-1703) | none |
| Container buffers | `#__track_buffer` typed regions (Vector/Array/HashMap, `vector.penguin:26-96`) | none |
| Meta/JIT | `_emperor_gc_pin_object` baked addresses (gc.c:1085-1109); JIT units allocate through the same `_emperor_alloc_impl` via `-rdynamic` | none |
| Quarantine ring | 512 most recent allocations pinned per cycle (gc.c:638-650) — covers SSA temporaries in flight | kept initially; retirement is gated on long-run `GC_VERIFY` evidence (§16) |
| Conservative stack cover | word-scan of main + coroutine stacks (gc.c:1821-1831, 2836-2844), register spill via `setjmp` (gc.c:2984-2986) | kept as safety net. In a non-moving collector a conservative hit is merely an extra mark — **no pinning machinery is needed**, which is what lets v3 delete the v2 pin/demote/tombstone code |

`GC_VERIFY` differential machinery (gc.c:3094-3265) is extended with the two
span invariants of §9.1; the existing conservative-vs-precise differential
keeps validating root completeness.

## 11. Write barriers and ABI

- **M4c (done)**: the emitter no longer emits barrier calls (the
  field-store and fallback-store sites in LLVMEmitter.penguin); the
  runtime keeps no-op symbol bodies so previously emitted `.ll` (and
  `.penguin-lib` consumers) still link and run. Symmetrically, code
  emitted *without* barriers is only correct with a v3 runtime — guarded
  by the ABI tag.
- Runtime ABI tag (compared on mixed dynlib builds): `emperor-rt-gc2-3d`
  → **`emperor-rt-gc3-gt1`** (bumped at M4c).
- JIT (`penguin_jit.cpp`, ORC `GetForCurrentProcess`) and dynlib consumers
  bind `_emperor_alloc_impl`/`_emperor_gc_poll`/frame-head from the host exe
  exactly as today; no new exported symbols are required by the design (all
  `gt_*` functions are internal).
- JIT (`penguin_jit.cpp`, ORC `GetForCurrentProcess`) and dynlib consumers
  bind `_emperor_alloc_impl`/`_emperor_gc_poll`/frame-head from the host exe
  exactly as today; no new exported symbols are required by the design (all
  `gt_*` functions are internal).

## 12. Modes and knobs

| Knob | Values / default | Meaning |
| --- | --- | --- |
| `EMPEROR_GC_MODE` | `greentea` — **the default** (unset/unknown) / `conservative` (emergency fallback, retained). `precise`/`legacy` were DELETED with the v2 generational machine at M4b — they warn and fall back to greentea (bisect with an older tree) | collector selection (gc.c init wiring) |
| `EMPEROR_GC_GOAL_FACTOR` | `2` | unified-goal multiplier over the COMBINED malloc+span live bytes (v3.1, §9.2) |
| `EMPEROR_GC_MIN_HEAP` | `4194304` | unified-goal proportional-floor (bytes) |
| `EMPEROR_GC_SPAN_CACHE` | unlimited | max spans kept on the empty pool before supers are released (0 = v2 retain-forever behavior, default) — NOT yet implemented; supers are retained for the process lifetime |
| `EMPEROR_GC_YOUNG` | `13421772` (128 MiB) | the unified goal's ADDITIVE churn slack (one nursery budget on top of the factor×live base, §9.2); tight-memory hosts can lower it |
| `EMPEROR_GC_STRESS_EVERY` | off | collect every N allocations (existing, gc.c:3407) |
| `EMPEROR_GC_DISABLE` / `GC_VERIFY` / `EMPEROR_GC_NO_STACK_COVER` | existing | unchanged semantics (gc.c:74-78, 3364, 3395-3402) |

`gc_info()` / `_emperor_gc_info_split` (gc.c:3427-3447) gains additive
fields (M3): `_emperor_gc_gt_stats(which)` (C) exposed as the penguin
builtin `gc_gt_stats(which: i64) -> i64` (`__builtin` extern, registered in
SemanticModel alongside gc_info; greentea-mode-only counters — 0 outside):
0 live slot bytes, 1 goal bytes, 2/3/4 full/partial/empty span counts,
5 wholesale-recycled spans (cumulative), 6 representative fast-path scans
(cumulative), 7 completed cycles, 8 superchunks. `Tests/GcTest/
GreenTeaSpanStats.md` asserts the counters end-to-end.

## 13. Key function reference

New (all in `gc_span.c`, internal linkage except where noted):

| Function | Purpose |
| --- | --- |
| `gt_class_of[slot]` / `gt_classes[23]` | size-class table + O(1) slot→class lookup (built once at init) |
| `gt_span_of(p)` / `gt_in_super_range(p)` | O(1) mask + exact-range + magic validation (§4.3) |
| `gt_slot_user_base / gt_slot_index / gt_slot_header` | interior-pointer → owning slot (§7.1) |
| `gt_alloc_small(size, slot)` | fast path (§6.2); called from `_emperor_alloc_impl` |
| `gt_refill_current(cls)` | slow path (§6.3); owns emergency collect |
| `gt_super_acquire / gt_span_reclass` | superchunk cutting; empty-span reclassing |
| `gt_mark(p)` | root visitor entry (§7.1); plugged into the root walks |
| `gt_gray_slot / gt_enqueue / gt_rep_list_push/remove` | color bookkeeping (§7.2) |
| `gt_scan_span / gt_scan_object / gt_drain` | the mark loop (§7.3) |
| `gt_sweep_span / gt_sweep_all / gt_run_finalizer` | sweep + finalizers (§8) |
| `gt_collect(emergency)` | cycle driver (§9.1) — implemented as `gc_collect_greentea` in gc.c (shares stats/root walks); called from `_emperor_gc_poll` and `gc_collect()` |

**M1 sweep phasing** (the load-bearing ordering; `gc.c`): the v2
`_emperor_gc_sweep` was split into `gc_sweep_pass1` (unlink + finalize)
and `gc_sweep_pass2` (index compact + free). The greentea driver runs:
malloc pass1 → `gt_sweep_finalize` (span dead-slot finalizers; dead malloc
bodies intact, un-freed) → malloc pass2 → `gt_sweep_reclaim`. Every
finalizer, either heap, observes every dead object intact, and
finalizer-time allocations (malloc path via the collecting latch) are
never visited by this cycle's unlink walk — no per-object marked hacks.

Modified in `gc.c`:

| Site | Change |
| --- | --- |
| `_emperor_alloc_impl` (~gc.c:3540-3611 region) | route small requests to `gt_alloc_small` in greentea mode |
| `_emperor_gc_poll` (gc.c:3581-3611) | trigger check → `gt_collect` |
| mode init (gc.c:3366-3381) | parse `greentea`, select heap flavor at `_emperor_gc_init` |
| root walks (frame chains gc.c:1138-1163, registry, regions, cover) | leaf action pluggable: `_emperor_gc_mark_object` ↔ `gt_mark` |
| `_emperor_gc_write_barrier(_map)` (gc.c:3474-3494) | no-op in greentea mode (§11) |
| `gc_info` split struct (gc.c:3427-3447) | additive GT stats fields |

Deleted at M4b (v2 machinery, ~1550 lines): `gc_minor` + the whole
evacuation machine (promote/tombstone/pin/demote, evac worklist,
evacuate/pin frame-chain walks), the nursery (YoungChunk/YoungSuper, chunk
supply, chunk index, bump allocation, maybe_nursery/super ranges), the
card table + dirty-card scan + barrier old-target logic (the barrier
SYMBOLS stay as ABI no-ops), the pending-old membership hash,
`gc_collect_generational`, the rescue-reporting context, the EVACUATE mode
of the ref-map walk, and the `precise`/`legacy`/`EMPEROR_GC_NOGEN` /
`EMPEROR_GC_RS_FALLBACK` / `EMPEROR_GC_STACK_COVER` /
`EMPEROR_GC_REGION_PINS` knobs (their machinery is gone). `conservative`
(inline-collect emergency fallback) is retained. `EMPEROR_GC_YOUNG` lives
on as the garbage-rich goal cap (§9.2).

## 14. Testing and benchmarks

- **Correctness hammer**: `EmperorPenguin/std/c/gc_torture.c` (746 lines,
  standalone) extended with span-heap workloads: linked-list churn (infant
  mortality), tree rebuild, shared DAG, container churn (Vector/HashMap),
  deep recursion with live frames, finalizer storms, string churn,
  mixed sizes across the 512 B boundary, and `EMPEROR_GC_STRESS_EVERY=1`
  variants of all of the above. A `greentea_section` asserts the M1
  surface: span/malloc-path graph integrity, exact finalizer counts, and
  exact wholesale-reclaim deltas (`_emperor_gc_alloc_charge` charges the
  size-class slot in greentea, keeping the exact-delta assertions
  mode-portable).
  **ASan test discipline**: with `-fsanitize=address`, locals may live on
  ASan's *fake stack* (heap-allocated frames) — `_emperor_gc_init(&local)`
  then brackets a non-stack range and the conservative cover finds
  nothing (`ASAN_OPTIONS=detect_stack_use_after_return=0` restores it).
  Tests must root handles via `_emperor_gc_add_root`/statics (the emitted
  main passes `llvm.frameaddress(0)`, the frame top, so production is
  unaffected — same discipline the v2 generational sections already
  follow).
- **e2e**: the 10 `Tests/GcTest/*.md` cases run with `EMPEROR_GC_MODE=greentea`
  (the env-driven mechanism already exists in that suite); new
  `Tests/GcTest/GreenTea*.md` cases assert `gc_info()` wholesale-recycle
  counts. Full `make test` matrix + `make unittest` green at every milestone.
- **Bootstrap**: `.ll` emission is unchanged until M4, so pass2–pass5 md5
  convergence is unaffected by M1–M3; M4 (barrier emission removal) requires
  one full `make bootstrap` re-convergence.
- **The M4 default-flip gate found two bugs the opt-in gates could not**
  (both fixed; both persisted as sentinels):
  1. **Ring-queue wrap+grow** (gc_span.c): growing the FIFO while the ring
     was wrapped orphaned the prefix written under the old modulus —
     `gt_dequeue` then walked uninitialized slots (pass3 self-compile
     SIGSEGV; deterministic repro shape in gc_torture's greentea section:
     PAIRED nodes whose edges target both mates of a pair guarantee ≥2
     grays per target span, so the drain's BFS frontier outruns
     consumption; sparse/group-crossing random edges do NOT fire it — the
     representative path dominates). Fix: unwrap on grow (memmove the
     wrapped prefix to the fresh tail).
  2. **Enum globals were never rooted** (LLVMEmitter emit_main): an
     enum-typed global's inline {meta, tag, payload} struct can hold
     references in its payload, but the registration loop only covered
     value-class structs and ref/string slots (LambdaCaptureGcOptionPayload
     — closure in `Option<fun>` freed every cycle; the v2 nursery had
     masked it via promotion). Fix: register enum globals as conservative
     scan regions.
- **Benchmarks (gate for the default flip)**: `make gc-bench` (target added
  at M0 — `EmperorPenguin/std/c/gc_bench.sh`) drives `gc_torture bench
  <workload>` with `GC_PROFILE` phase timing (mark / sweep / total pause).
  Gates: ≥15% mark-phase CPU reduction on pointer-dense and container
  churn workloads; ≤5% regression on deep-survival workloads (the known
  nursery-removal trade-off), measured against v2 `precise` on the same
  binaries.

**M0 baseline (v2 `precise`, recorded 2026-09; dev host linux x86_64,
clang release build, medians of one `make gc-bench` run — the same-run
relative deltas are what the M3 gate compares):**

| workload | majors | mark_ms | sweep_ms | gc_total_ms | allocs | live peak |
| --- | --- | --- | --- | --- | --- | --- |
| churn | 16 | 39.0 | 0.001 | 39.0 | 5.00 M | ~0 MiB |
| ptrdense | 16 | 200.6 | 0.006 | 309.8 | 2.04 M | 3.2 MiB |
| container | 12 | 12.3 | 0.457 | 12.7 | 1.50 M | 0.1 MiB |
| deepsurvive | 160 | 8514.3 | 9.7 | 9084.2 | 3.86 M | 55.0 MiB |
| finstorm | 60 | 4.8 | 0.001 | 4.8 | 1.20 M | ~0 MiB |
| mixed512 | 8 | 58.5 | 0.023 | 58.6 | 1.00 M | ~0 MiB |

Workload shapes (all deterministic, `gc_torture.c::bench_*`): `churn` =
4M infant-mortality small+brief-string allocs with explicit collect every
262143 iters; `ptrdense` = 30k persistent node list + relink churn, 2M
iters, collect every 131071; `container` = 32 typed buffers × 64 slots,
1.5M element rewrites, collect every 131071; `deepsurvive` = forest growing
to 655k live nodes (~55 MiB) over 160 explicit collects + inter-batch churn;
`finstorm` = 60 rounds × 20k destructor-stamped objects killed per round;
`mixed512` = 1M allocs with bodies uniform in 200..600 B straddling the
span cutoff, collect every 131071.

**M2 numbers (greentea span-batched marking + bitmap sweep, same host,
same-day v2 comparison):**

| workload | v2 mark_ms | M2 mark_ms | mark Δ | v2 total_ms | M2 total_ms |
| --- | --- | --- | --- | --- | --- |
| churn | 39.0 | 0.8 | −98% | 39.0 | 18.2 |
| ptrdense | 200.6 | 37.6 | −81% | 309.8 | 42.4 |
| container | 12.3 | 1.9 | −85% | 12.7 | 5.0 |
| deepsurvive | 8514.3 | 1742.4 | −80% | 9084.2 | 1842.1 |
| finstorm | 4.8 | 0.1 | −98% | 4.8 | 4.5 |
| mixed512 | 58.5 | 40.6 | −31% | 58.6 | 51.5 |

**M3 numbers (goal heuristics + garbage-rich amortization + gc_gt_stats):**

| workload | v2 majors | M3 majors | v2 mark_ms | M3 mark_ms | mark Δ | v2 total_ms | M3 total_ms |
| --- | --- | --- | --- | --- | --- | --- | --- |
| churn | 16 | 18 | 39.0 | 0.08 | −99.8% | 39.0 | 22.9 |
| ptrdense | 16 | 46 | 200.6 | 29.4 | −85% | 309.8 | 33.8 |
| container | 12 | 13 | 12.3 | 0.6 | −95% | 12.7 | 3.4 |
| deepsurvive | 160 | 160 | 8514.3 | 1800.7 | −79% | 9084.2 | 1895.9 |
| finstorm | 60 | 60 | 4.8 | 0.05 | −99% | 4.8 | 2.6 |
| mixed512 | 8 | 12 | 58.5 | 8.0 | −86% | 58.6 | 39.4 |

The M3 gates (mark −15% on pointer-dense/container; deep-survival total
regression ≤5%) are exceeded by a wide margin — including deep-survival,
which is 4.8× FASTER than v2 (the nursery's copying dividend is more than
replaced by wholesale span recycling + the batched marker). The M3 tuning
replaced the flat-goal over-collection on churn shapes (mixed512 168 → 12
cycles, churn 77 → 18) with nursery-parity amortization at ≤128 MiB RSS.

**v3.1 numbers (unified dual-heap goal, §9.2 — same host, same method):**

| workload | v2 majors | M3 majors | v3.1 majors | M3 total_ms | v3.1 total_ms |
| --- | --- | --- | --- | --- | --- |
| churn | 16 | 18 | 16 | 22.9 | 39.7 |
| ptrdense | 16 | 46 | 16 | 33.8 | 15.6 |
| container | 12 | 13 | 12 | 3.4 | 3.4 |
| deepsurvive | 160 | 160 | 160 | 1895.9 | 1895.5 |
| finstorm | 60 | 60 | 60 | 2.6 | 3.7 |
| mixed512 | 8 | 12 | 8 | 39.4 | 36.4 |

v3.1 restores v2's collection cadence everywhere (ptrdense 46 → 16,
mixed512 12 → 8) via the young-budget churn slack, and eliminates the
M4-era real-world wall regressions (see the §14 real-world table in
`/var/tmp/gcbench/report.md`; medians of 3): bootstrap 52.6s → 48.1s
(v2: 51.6s — the +1.9% regression becomes −6.8%), tinyriscv compile
20.5s → 17.7s (v2 18.9s), LSP 40.4s → 38.8s (v2 43.7s), tinyriscv
simulation 1.30s → 1.28s (v2 1.79s). Peak RSS rises vs v3.0 (bootstrap
1031 → 1231 MiB, LSP 1100 → 1247 MiB) as the price of the slack, still
below v2 everywhere (1423/1306 MiB); peak heap is bounded at
2×live + young by construction.
- **Platforms**: linux x86_64 + aarch64, `CROSS=win64` (llvm-mingw) — all
  span code is plain C; the only arch-sensitive pieces are the existing
  stack-pointer asm (gc.c:272-288) and `_aligned_malloc` vs `aligned_alloc`.

## 15. Milestones

| M | Deliverable | Gate |
| --- | --- | --- |
| M0 | benchmark harness + v2 baseline numbers (`make gc-bench`, gc_torture extension) | baseline table recorded — **done** |
| M1 | span heap **allocation** side (§4–§6); marking still per-object over spans | gc_torture + full `make test` + `make unittest` — **done** |
| M2 | green-tea mark loop + sweep (§7–§9) behind `EMPEROR_GC_MODE=greentea` | previous gates + all GcTest under greentea + bootstrap unchanged — **done** |
| M3 | goal heuristics, `gc_info` stats, tuning | benchmark gates (§14) — **done** (mark −79..−99.8%, deepsurvive 4.8× faster) |
| M4 | default flip; delete v2 generational machine; stop emitting barriers; ABI tag bump; docs | all gates + both platforms + bootstrap re-convergence — **done** (M4a flip + 2 flip-gate bug fixes; M4b deletion; M4c barrier retirement + tag bump; bootstrap re-converged; make test 1563/0) |
| v3.1 | unified dual-heap goal (§9.2 erratum): ONE budget over malloc+span, `max(min_heap, factor×live) + young`, delete `gt_update_goal` | same gates re-run — **done** (gc-bench cadence at v2 parity: ptrdense 46→16, mixed512 12→8; bootstrap wall −6.8% vs v2 (52.6→48.1s), GC 12.3→8.0s, 19 fulls; all three real-world loads beat v2 on wall AND RSS; make test 1563/0) |

## 16. Future work

- **SIMD bitmap kernels**: `gray & ~black` over 64-byte AVX-512/GFNI blocks
  (the `VGF2P8AFFINEQB` trick from the Go blog), x86_64 only, CPUID-gated
  with the scalar kernel as fallback. Go reports ~10% further reduction.
- **Headerless slots**: derive size/class from the span, keep metadata only
  for finalizable classes — recovers the 24 B per object.
- **`_emperor_alloc_impl(size, flags)`**: additive ABI so codegen can mark
  finalizer-free allocations, making wholesale span recycle O(1) (no
  destructor probe).
- **Retiring safety nets**: quarantine ring and conservative stack cover can
  be removed once `GC_VERIFY` differentials run clean over the full test
  corpus + bootstrap + gc_torture for an extended period.
- **Concurrent marking**: only meaningful with multiple OS threads; would
  reintroduce write barriers (snapshot-at-beginning) — explicitly out of
  scope while the runtime is single-mutator.

## 17. References

- golang/go#73581 — runtime: green tea garbage collector (design + data):
  <https://github.com/golang/go/issues/73581>
- Go blog: The Green Tea Garbage Collector:
  <https://go.dev/blog/greenteagc>
- Go 1.25 release notes (experiment), Go 1.26 (default):
  <https://go.dev/doc/go1.25>, <https://go.dev/doc/go1.26>
- Current implementation: `EmperorPenguin/std/c/gc.c` (v2), `gc_torture.c`,
  `include/emperor_gc.h`, `include/emperor_types.h` (ref-map encoding),
  `scheduler.c` (coroutine stacks), `penguin_jit.cpp` (JIT allocation path)
- Emitter side (unchanged by this design): `EmperorPenguin/src/llvm/LLVMEmitter.penguin`
  — poll emission (4475-4482), frame descriptors (1642-1667), metadata/ref-map
  tables (2637-2845), alloc sites (4934/5123/5489/5507), barrier sites
  (4289-4318)
- Implementation plan: `.agents/plans/gc-greentea-span-heap.md`
