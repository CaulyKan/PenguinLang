# 29. EmperorPenguin GC — Green Tea Span Collector

The EmperorPenguin runtime garbage collector is a **single-generation,
non-moving, stop-the-world mark/sweep collector** built around a
**green-tea span heap** (the marking strategy Go reworked for small
objects, golang/go#73581, [`go.dev/blog/greenteagc`](https://go.dev/blog/greenteagc)):
instead of queuing individual object pointers, the marker queues **whole
8 KiB spans** of one size class and scans them with linear, prefetch-friendly
bitmap passes.

Code map:

| File | Contents |
| --- | --- |
| `EmperorPenguin/std/c/gc_span.c` | The span heap: size classes, superchunk supply, per-thread arenas, allocation, O(1) address→span, gray/black bitmap marking, sweep |
| `EmperorPenguin/std/c/gc.c` | Root discovery (frame chains, globals, regions, quarantine), the precise ref-map walk, the malloc large-object heap with its sorted interior-pointer index, the collection driver, the unified heap goal, the hard-cap valve, the safepoint poll |
| `EmperorPenguin/std/include/gc_internal.h` | Internal interface shared by the two units (GCHeader, layout constants, `gt_*` API) |
| `EmperorPenguin/std/include/emperor_gc.h` | Public runtime API (`_emperor_gc_init`, roots, buffers, polls, introspection) |

The collector is the only one in the tree; there is no mode switch. The
runtime ABI tag is `emperor-rt-gc3-gt2` (see §9).

## 1. Heap layout

### 1.1 Two heaps

| | Small objects | Large objects |
| --- | --- | --- |
| Boundary | slot = `24B GCHeader + body`, rounded to a size class, ≤ 512 B | everything bigger |
| Storage | 8 KiB spans cut from aligned superchunks (gc_span.c) | `malloc` blocks on `_emperor_gc_allocation_list` (gc.c) |
| Interior-pointer resolution | O(1): range test + 8 KiB mask + magic (`gt_slot_owner`) | binary search over the incrementally-maintained sorted start-address index (`gc_resolve_block`) |
| Mark color | gray/black bits in the span's bitmaps | `GCHeader.marked` + the shared mark worklist |

Strings allocate through the same router (`_emperor_string_alloc` takes
the span path when the slot fits a class).

### 1.2 Superchunk → span

```c
#define GT_SPAN_SIZE    8192              /* one span, 8KiB-aligned        */
#define GT_SPAN_HDR     128               /* sizeof(GtSpan), fixed         */
#define GT_BITMAP_WORDS 4                 /* 4*64 = 256 slots max          */
#define GT_SUPER_SPANS  128               /* 128 * 8KiB = 1MiB payload     */
#define GT_SUPER_SIZE   (GT_SPAN_SIZE * (GT_SUPER_SPANS + 1))
```

A superchunk is over-allocated by one span and aligned up internally
(`gt_super_alloc_bytes` — no `aligned_alloc` availability constraints,
identical on every platform). The **first 8 KiB** holds the `GtSuper`
bookkeeping block; the remaining 128 spans land exactly on 8 KiB
boundaries.

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
│ class,nslots,bitmaps,flags... │ 32..512 B each                     │
└───────────────────────────────┴────────┴────────┴────────┴─────┴────────┘
```

### 1.3 Size classes

23 classes covering slot sizes 32..512 B (Go's table minus the classes
below our 32 B minimum header+word slot):

```
slot sizes: 32 48 64 80 96 112 128 144 160 176 192 208 224 240 256
            288 320 352 384 416 448 480 512          (23 classes)
```

`gt_class_of[slot>>3]` is a byte table built once (`gt_build_classes`),
so slot→class is a single load. Slot counts per span follow
`floor(8064 / slot_size)` (e.g. 252 × 32 B, 31 × 256 B, 15 × 512 B).

### 1.4 Object layout

Every slot begins with the 24 B `GCHeader` (`{next, marked, is_string,
size}`) followed by the body whose word 0 is the `EmperorClassMetadata*`
stamped by codegen right after allocation (strings instead carry
`_emperor_string_metadata*` + length + data; `is_string` blocks all body
walking). In spans, `next`/`marked` are redundant (pool links and colors
live in the span) — accepted overhead; headerless slots are future work
(§11). Interior pointers (enum payloads at +16, nested value classes)
root their **owner** slot under the same containment rule on both heaps.

### 1.5 Address → span (O(1))

Spans are 8 KiB-aligned by construction, so resolution is a mask plus
validation (`gt_span_of`):

```c
static GtSpan* gt_span_of(const void* p) {
    if (!gt_in_super_range(p)) return NULL;            /* EXACT ranges    */
    GtSpan* s = (GtSpan*)((uintptr_t)p & ~(uintptr_t)(GT_SPAN_SIZE - 1));
    if (s->magic != GT_SPAN_MAGIC) return NULL;       /* false-hit guard   */
    return s;
}
```

`gt_in_super_range` binary-searches the sorted **exact super ranges**
(recorded at `gt_super_acquire`; a one-entry cache fronts it — marking's
reference stream is strongly span-local). The loose `[heap_lo, heap_hi)`
span would cover unmapped gaps between the supers' allocations, and
`gt_span_of` **dereferences** the masked base — a candidate landing in a
gap would SIGSEGV on the magic read. Membership in an actual super makes
the deref safe by construction (supers are never freed; ranges only
append). The magic check also rejects the super's bookkeeping block (a
distinct `GT_SUPER_MAGIC` at offset 0 fails it).

## 2. Allocation

### 2.1 Per-thread arenas

Allocation state is **per-thread** (`GtArena`, gc_internal.h): the
per-class current span, the thread's fresh-object quarantine ring, and an
unflushed `alloc_bytes` delta. Arenas are registered once into a fixed
pool of 64 (`gt_arena_thread`, under `gc_alloc_lock`; the TLS pointer
serves the fast path without locking). The arena survives its thread:
the stop-the-world sweep walks the registry, not the live threads.

### 2.2 Fast path

`_emperor_gc_alloc(size, is_string)` routes small objects to
`gt_alloc_small` (gc_span.c), which:

1. computes the class from the rounded slot size;
2. takes the arena's current span and scans its `alloc_bits` from the
   `free_index` hint for the first clear bit;
3. stamps the header, zeroes the body, bumps `alloc_bytes`
   (arena-local, no shared writes);
4. pushes the fresh pointer onto the arena's quarantine ring;
5. checks the unified heap goal (`gc_unified_goal_reached`) and raises
   `_emperor_gc_want_collect` when reached — idempotent until a poll
   drains it.

No lock is taken anywhere on this path; the arena's current span is
exclusively owned by the thread.

### 2.3 Slow path (retire + refill)

When the current span has no free slot, `gt_retire_refill` (under
`gc_alloc_lock`) folds the arena's `alloc_bytes` into the global
approximate counter, retires the exhausted span to the shared full list,
and takes a fresh one: shared partial → empty pool (re-classed on demand,
so classes cannot strand memory) → new superchunk. On superchunk
failure it runs an emergency collection **outside** the lock (collect-time
allocations take the malloc path, and the lock is not recursive) and
retries once; mid parallel-delta the emergency collect is skipped and the
boundary collects instead (§7.3). Allocation never clears alloc bits — only the sweep does —
so freed slots are reusable only after that sweep.

### 2.4 The malloc large path

Large objects (and small-object fallthroughs: superchunk OOM, registry
exhaustion, allocations made *during* a collection, where the in-progress
sweep must not judge them via the span bitmaps) go through
`gc_alloc_locked`: a zeroed `malloc` block linked onto
`_emperor_gc_allocation_list`, appended to the sorted index's pending set
(`gc_pending_append`), and pushed onto the arena's quarantine ring.

The sorted index is maintained **incrementally**: allocations append to a
small pending set; each collection sorts just that set and back-merges it
into the persistent sorted array; the sweep compacts dead entries out in
the same pass (`gc_refresh_sorted` / `gc_sweep_pass2`). A pending-append
failure marks the index stale and the next refresh rebuilds it whole from
the authoritative list — a missing block would let marking free a live
object, so the fallback is mandatory.

### 2.5 Hard cap

`EMPEROR_GC_HARD_CAP` (default 8 GiB, 0 = unlimited) bounds the heap
when collections are deferred by a long parallel delta: the allocator
refuses the charge, the touching coroutine parks on the scheduler's
"wait for GC" list (`_emperor_sched_alloc_park` — not runnable, which is
exactly what converges an all-blocked delta into the boundary's
stop-the-world collection), and the allocation retries after the freed
space lands. Where no coroutine can park (main-stack code, the classic
scheduler) the park returns 0 at once and the goal cadence applies
unchanged — the cap is advisory wherever the valve cannot act. A single
allocation larger than the cap itself aborts deterministically
(`_emperor_gc_oom_abort`, exit code 62); the boundary's OOM predicate
`_emperor_gc_cap_blocks` compares live + charge so parkers cannot stall
forever in the gap between the two.

## 3. Roots

| Root source | Mechanism |
| --- | --- |
| Stack frames (all stacks) | `EmperorGcFrame` chains linked onto `_emperor_gc_frame_head` (thread-local; the scheduler swaps it per coroutine and restores it on sjlj throws). Each slot is either a bare pointer home (`map == NULL`: `*addr` is one reference) or an inline struct home walked with its ref-map. The scheduler's `_emperor_sched_each_frame_head` enumerates every PARKED coroutine's saved chain. |
| Globals | `_emperor_gc_add_root(void**)` registry (swap-remove on unregister); value-class/enum globals additionally register their whole struct as a scan region. |
| Native arrays `array<T>` | First-class GC objects — no registration needed. Layout `{ ptr metadata, i64 length, elements... }`; the per-element-type metadata's refmap is a REFARR node (kind 3: `elem_stride`, `elem_sub`), and the walker reads the live count at object+8 and walks each element with `elem_sub` (primitive elements are a precise no-op, not a conservative scan). Containers (List/Vector/HashMap) are backed by `array<T>` slots now, so container graphs trace precisely through ordinary object marking. |
| Coroutine stacks | Registered as regions by the scheduler, narrowed to `[parked_sp, top)` via `_emperor_gc_scan_set_live` so dead frames below a parked sp — stale garbage a conservative scan would pin forever — are excluded; the RUNNING coroutine's region is capped at the collect-time sp. |
| Quarantine ring | A bounded ring (512 entries) of each arena's most recent allocations — covers freshly allocated objects that are reachable only from unhomed SSA temporaries (the `this.f = new Outer(new Inner(...))` shape: the Inner result has no slot and no heap home until the statement completes, yet polls sit between the two allocations). Explicit `gc_collect()` cycles DROP the rings (collect-now semantics — young garbage must die; the conservative cover below holds the in-flight temps); automatic cycles consume them. |
| Conservative stack cover | Word-scan of the main stack(s) — always on. Safe in a non-moving collector: a hit is merely an extra mark, so no pinning machinery exists. Each registered thread contributes `[base, boundary watermark)`; the collecting thread's own stack is scanned from the sp (spilled registers included via a `setjmp` flush) up to its watermark. |

## 4. Marking

### 4.1 Tricolor in span bitmaps

A slot's color lives in its span's bitmaps — white = neither bit, gray =
reachable/unscanned, black = reachable/scanned — so setting/testing a
color is a shift+or on a 64-bit word shared by 64 neighboring objects;
no per-object header traffic during marking. `GCHeader.marked` stays in
use only for the malloc large path (the shared worklist in gc.c).

### 4.2 The mark leaves

Every root walk resolves a candidate pointer to its owner and grays it:

* `gt_mark_candidate(p)` (gc_span.c) is the hot leaf: ONE range lookup
  decides span vs large — membership, owner resolution and the gray are
  merged precisely because the paired form paid the super-range binary
  search twice per reference word.
* Non-span candidates fall back to `gc_resolve_block` (sorted index) and
  the per-object mark+push.
* Root walks that hold an exact user pointer go through
  `_emperor_gc_mark_object` → `gt_gray_object`.

### 4.3 Span graying: queue + representative fast path

`gt_gray_slot` dedups on the gray bit and then does representative
bookkeeping:

* a span with its **first** gray object is not enqueued at all — it sits
  on the representative array with `rep_slot` set and the lone object is
  scanned directly at drain time (`n_rep_scans` counts the hits; in real
  heaps many spans have exactly one live object per cycle);
* a **second distinct** object flips `REP_HIT` and enqueues the span for
  a real bitmap scan (the FIFO queue dedups with `GT_F_ENQUEUED`).

The representative bookkeeping uses an ARRAY, not a linked list through
`GtSpan.next`: during marking a span's `next` is its POOL link
(current/partial/full), so it cannot double as a mark-structure link. A
re-grayed candidate after the span left both structures clears the stale
`REP_HIT` before re-pending.

### 4.4 The drain loop

`gt_drain` runs to gray == black globally: the large-object worklist
first (its walks gray span slots), then queued spans, then lone
representatives. A span scan (`gt_scan_span`) computes
`gray & ~black` word-at-a-time, blackens the batch, and scans exactly
those objects; scanning can gray new slots in the same span, so the scan
re-passes until the span is locally quiet. The queue is FIFO, so spans
are scanned in roughly ascending address order — the cache-miss
reduction is the mechanism Green Tea is named for.

Object bodies are scanned precisely through the per-type **ref-map
program** (`gc_refmap_walk`; encoding in `emperor_types.h`): only the
pointer locations the emitter declared are read. Mapless bodies (foreign
metadata, or a freshly allocated block between zeroing and metaptr
stamping) fall back to a conservative whole-body word scan. Corrupt
ref-map programs (offsets outside the object, enum tags outside the
variant table, REFARR extents beyond the object) abort loudly with
forensics. A REFARR node with `elem_sub == 0` (pointer-free elements) never
reads the element area — spare capacity slots are invisible to the marker.

**Failure policy**: if any worklist/queue growth fails mid-mark,
`_emperor_gc_mark_failed` is set, marking is partial, and the cycle
retains everything (no sweep) — the next cycle re-judges from scratch.

`GC_VERIFY=1` additionally asserts the span invariants after every drain
(`gt_verify_invariants`: gray == black per word, black ⊆ alloc) and
warns once per mapless type name.

## 5. Sweep

After a successful mark, black = live. No finalizers exist (the
IMemoryDispose/destructor machinery was retired with the native-arrays
round — files close explicitly, containers are GC-traced `array<T>` objects),
so the sweep runs no user code at all and needs no phase ordering:

1. `gc_sweep` (gc.c, single pass) — walk the malloc list, unlink dead
   blocks into a pending chain (tombstoned `marked == 2`), compact the
   sorted index in the same walk, then `free()` the chain.
2. `gt_sweep_reclaim` (gc_span.c) — clear dead alloc bits, recompute live
   counts, repool each span: **wholesale** recycling to the class-agnostic
   empty pool when `live == 0` (infant-mortality workloads mostly produce
   dead spans, so reclamation cost drops from O(objects) to O(spans)),
   otherwise partial/full.

Collect-time allocations take the malloc path (the collecting latch
suppresses nested collection) and are never visited by this cycle's unlink
walk.

## 6. Collection cycle

`gc_collect_greentea` (gc.c) is the single driver; every entry — poll
drains, stress collects, the emergency slow path, explicit
`gc_collect()` and the scheduler boundary — funnels through
`gc_collect_auto`:

```
gc_collect_greentea():
    guards: disabled / no stack / already collecting
    setjmp register flush; capture sp
    gc_refresh_sorted()                     # incremental index merge
    mark: global roots → frame chains (running + parked)
          → regions (conservative word-scan)
    gc_mark_drain()
    quarantine: explicit cycle drops the rings, auto cycle consumes them
    conservative stack cover (this thread's watermark + every other
          registered thread's parked range)
    gt_drain()                              # to gray == black
    [GC_VERIFY] gt_verify_invariants()
    on mark failure: reset all colors, retain everything, return
    sweep: gc_sweep (unlink+compact+free) → gt_sweep_reclaim
    gc_update_unified_goal(freed)
```

Coroutine safety: a collection runs only inside a safepoint poll on the
running thread (or at the scheduler boundary with every worker parked);
all other stacks are parked, their frame chains complete and enumerable,
their scan regions narrowed to `[parked_sp, top]`.

## 7. Triggers and the unified heap goal

### 7.1 The goal formula

ONE budget covers BOTH heaps (the trigger compares malloc-allocated +
span-allocated bytes against it, and the post-sweep update recalculates
it from the COMBINED live set):

```
goal = max(EMPEROR_GC_MIN_HEAP, EMPEROR_GC_GOAL_FACTOR × combined_live)
       + EMPEROR_GC_YOUNG
```

* the proportional base (default factor 2) is the classic 2×-live
  threshold;
* the additive young budget (default 128 MiB — the measured knee on the
  compiler self-compile) is guaranteed churn slack between collections:
  a pure factor × live goal pins the garbage fraction near
  1/(1+factor) every cycle, which a ramping live set pays at the ramp
  integral;
* live-heavy sweeps (freed < live/4 — the LSP-recompile shape, where each
  cycle costs O(live blocks) while freeing almost nothing) grow the goal
  ×4, capped at 4 TiB;
* the goal never shrinks. Peak heap is bounded at
  (1+factor) × live + young.

### 7.2 Regular trigger: the safepoint poll

The emitter places `_emperor_gc_poll` before every call and at every
allocation site. The steady state reads ONE word — the poll gate (bit
layout in `gc_internal.h`): zero returns, non-zero enters the slow path.
Every "this poll has something to do" condition is folded into that word:
collection requests go through `gc_request_collect()` (the semantic flag
`_emperor_gc_want_collect` and the gate rise together), the env-derived
stress / poll-counter bits are lit at init, the collect driver lowers the
gate once per cycle, and the STW age-clock re-arm moved into the driver
along with it — so the fast path is one relaxed load and a not-taken
branch, with no store at all; it used to read five separate globals here.
When `_emperor_gc_want_collect` is raised (by either allocation side
crossing the goal) the poll runs the full cycle. The stress knobs
(`EMPEROR_GC_STRESS_EVERY` / `_MAX`) force a collect every Nth poll for
correctness hammering. The malloc path additionally collects inline at
8× the goal — a C-heavy stretch with no poll in sight.

### 7.3 Parallel deltas and the boundary

While the mt scheduler runs a parallel delta (`_emperor_sched_parallel_active`),
workers hold coroutine stacks mid-flight and no thread may collect:
allocation-side triggers only raise the flag, polls defer, and the
**scheduler boundary** (every worker parked — stop-the-world by
construction) drains it via `_emperor_gc_collect_now`. A flag the
boundary cannot drain (a busy service inside a perpetual delta)
escalates after `EMPEROR_GC_STW_MS` (default 250 ms, 0 = off): the
polling coroutine files onto the hard-cap valve's parked list, forcing
the convergence the boundary needs. The age clock starts at the first
poll that observes the flag and re-arms whenever the flag is down; the
re-arm is a conditional store because the drained steady state is the
per-back-edge hot path of every coroutine.

## 8. Introspection

| Symbol | Meaning |
| --- | --- |
| `gc_collect()` (penguin builtin) | explicit full cycle — "collect now" semantics: quarantine rings dropped, conservative cover on |
| `gc_info()` (penguin builtin) | exact managed bytes (malloc list + span heap including unflushed arena deltas) |
| `gc_gt_stats(which)` (penguin builtin) | 0 live slot bytes, 1 goal bytes, 2/3/4 full/partial/empty span counts, 5 wholesale-recycled spans (cumulative), 6 representative fast-path scans (cumulative), 7 completed cycles, 8 superchunks. `Tests/GcTest/GreenTeaSpanStats.md` asserts the counters end-to-end. |
| `_emperor_gc_info_split` (test-only) | malloc-heap bytes vs span-heap bytes |
| `_emperor_gc_alloc_charge` (test-only) | the heap charge of a tracked allocation (size-class slot, or header + round8 on the large path) — gc_torture's exact live/die deltas use it |
| `EMPEROR_GC_STATS=1` / `GC_PROFILE=1` | atexit phase/cost summary; the profile line feeds `make gc-bench` |

## 9. Emitted-code ABI

* Safepoint polls (`_emperor_gc_poll`) before every call and allocation;
  frame-chain link/unlink per function; ref-map programs and metadata
  globals per type (arrays included: one REFARR node per element type).
* The runtime ABI tag `_emperor_runtime_abi == "emperor-rt-gc3-gt3"` is
  compared on mixed dynlib builds so an emission never meets a foreign
  runtime silently.
* JIT modules and dynlib consumers bind the allocator/poll/frame-head
  symbols from the host exe via `-rdynamic`; all `gt_*` functions are
  internal to the runtime.

## 10. Knobs

| Knob | Default | Meaning |
| --- | --- | --- |
| `EMPEROR_GC_GOAL_FACTOR` | `2` | unified-goal multiplier over the combined malloc+span live bytes |
| `EMPEROR_GC_MIN_HEAP` | `4194304` | unified-goal proportional floor (bytes) |
| `EMPEROR_GC_YOUNG` | `134217728` (128 MiB) | the goal's additive churn slack; tight-memory hosts can lower it (floored at 64 KiB) |
| `EMPEROR_GC_HARD_CAP` | `8589934592` (8 GiB) | allocation pressure valve; 0 = unlimited |
| `EMPEROR_GC_STW_MS` | `250` | poll-side STW escalation age for perpetual parallel deltas; 0 = off |
| `EMPEROR_GC_STRESS_EVERY` / `EMPEROR_GC_STRESS_MAX` | off | collect every Nth poll; stop after M collects |
| `EMPEROR_GC_DISABLE` | off | raise the goal to 8 TiB so automatic collection never fires (diagnostics); explicit collects still run |
| `GC_VERIFY` | off | assert span invariants each cycle; warn on mapless types |
| `EMPEROR_GC_STATS` / `GC_PROFILE` | off | atexit cost attribution / machine-parseable bench line |

`EMPEROR_GC_MODE` is obsolete and ignored (a warning is printed when it
is set).

## 11. Testing and benchmarks

* **`gc_torture.c`** (standalone; build line in its header comment):
  correctness sections for the sorted index (interior pointers,
  address-recycling, checksum stability), precise ref-map walking with
  exact live/die deltas (poison words, stale enum payloads, tag flips),
  and a green-tea section (mixed span/malloc graph integrity, exact
  dead-slot reclamation deltas, ring-queue wrap+grow regression,
  wholesale reclaim). **Harness discipline**: the exact-delta assertions hold only
  when no stale copy of a to-be-freed pointer survives in a LIVE frame —
  the conservative cover reads every live-frame word. Pointers that a
  later assertion frees are therefore allocated, published and dropped
  inside helper functions whose frames die and are scrubbed
  (`scrub()` + `CLEAR_CALLEE_SAVED()`); a plain dead store
  (`x = NULL`) is not enough — compilers eliminate it.
  Under ASan, `ASAN_OPTIONS=detect_stack_use_after_return=0` keeps
  locals on the real stack (the fake stack would hide them from the
  cover).
* **`make gc-bench`** (`EmperorPenguin/std/c/gc_bench.sh`): builds
  gc_torture in release and drives the six deterministic workloads
  (churn / ptrdense / container / deepsurvive / mixed512),
  tabulating the `GC_PROFILE` phase-timing line per workload.
* **`Tests/GcTest/*.md`**: the cross-compiler e2e guards — enum globals
  and payloads under churn, native-array element tracing, container
  reclamation, write-chain/parameter rooting, span-stats introspection.
* The C runtime builds `-O2` by default (std/c Makefile `OPT`; the
  allocator and the scheduler's context switch are hot paths).
* Platforms: linux x86_64 + aarch64, `CROSS=win64` (llvm-mingw). The
  only arch-sensitive pieces are the stack-pointer asm and the timing
  clock (QPC vs `clock_gettime`).

## 12. Future work

- **SIMD bitmap kernels**: `gray & ~black` over AVX-512/GFNI blocks,
  CPUID-gated with the scalar kernel as fallback.
- **Headerless slots**: derive size/class from the span — recovers the
  24 B per object (nothing is finalizable anymore, so no class needs to
  keep metadata for the sweep).
- **Local/shared domain split**: per-arena local collectors with
  promotion at the scheduler handoff points, instead of one global
  stop-the-world pass over every arena.
- **Retiring safety nets**: the quarantine ring and the conservative
  stack cover can shrink once `GC_VERIFY` runs clean over the full
  corpus for an extended period.
- **Concurrent marking**: only meaningful with multiple OS threads;
  would reintroduce write barriers — out of scope.

## 13. References

- golang/go#73581 — runtime: green tea garbage collector (design + data):
  <https://github.com/golang/go/issues/73581>
- Go blog: The Green Tea Garbage Collector:
  <https://go.dev/blog/greenteagc>
- Emitter side: `EmperorPenguin/src/llvm/LLVMEmitter.penguin` (poll
  emission, frame descriptors, metadata/ref-map tables, alloc sites)
- Scheduler interop: `EmperorPenguin/std/c/scheduler.c` (boundary
  collection, parked-stack enumeration, the alloc-park valve)
