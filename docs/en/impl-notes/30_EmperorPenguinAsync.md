# 30. EmperorPenguin Async

EmperorPenguin implements the async/wait language surface (see the [Async & Timing Model spec](../specifications/09_AsyncAndTimingModel.md)) with **binder desugaring plus stackful coroutines (fibers)** — not LLVM state machines. This page documents the gate, the lowerings, and the two-layer native runtime.

## The Coroutine Gate

`enable_coroutine: bool = false` (`EmperorPenguin/src/bound/EmperorPenguinCompiler.penguin` lines 49–52) gates ports, `construct`, `connect`, every `wait` form, try/catch, and `async`. Any of them without the flag reports `E_UNSUPPORTED "requires the --enable-coroutine option"` through `require_coroutine` (`src/bound/SemanticModel.penguin` line 423) — verified by `Tests/PortTest/CoroutineSyntaxRequiresFlag.md`. BabyPenguin implements the same features unconditionally. Every wait/async also sets `unit.has_suspension = true` (`src/bound/BoundCompilationUnit.penguin` lines 28–31) — a wait-free program emits a plain sequential `main` regardless of the flag (LLVMEmitter.penguin lines 779–780).

## wait Lowering (SemanticBindExpressions.penguin)

`bind_wait_expr` (line 414+) desugars every wait form into ordinary control flow over two runtime primitives — `_co_wait()` (park one delta) and `IFuture.do_wait()`:

| Source form | Desugaring |
|---|---|
| `wait;` | `__builtin._co_wait()` |
| `wait <int expr>;` | `_after(n).do_wait()` |
| `wait ev` (`Event<T>`) | `cast<mut IFuture<T>>(new _EventSubscription<T>(ev)).do_wait()` |
| `wait <IFuture>` / `wait <port>` | cast + `.do_wait()` (`mk_do_wait_call`, lines 134–143) |

A bool operand and a `change(x)` operand are compile errors (`E_TYPE_MISMATCH`): the operand must be an integer (a timer), an `Event`, an `IFuture`, a channel, or a port.

Port bare-read settle points and the read permission matrix are handled in the same pass (the port channel field is read through the same future machinery).

## async Lowering (bind_spawn_async, ~lines 2300–2700)

Each `async f(args)` site synthesizes two helper classes per call site:

- `__SpawnCtx_<n>` implementing `__builtin.__ICoroutineEntry.__enter` — captures receiver and arguments, calls the function, completes the future with the result (`fut.complete(result)`).
- `__SpawnFut_<n>` — fields `done`/`result`, a `complete()` method, and the `IFuture<R>.poll()` implementation returning ready_finished/not_ready (`__SpawnUnitFuture` for void).

The call site becomes `{ let __s = new __SpawnCtx_...(args); __builtin._co_spawn_entry(__s); __s.fut }`. Errors: E_ASYNC_INVALID "'async' requires a function call" / "cannot spawn a static member call".

Generators lower on the same coroutine machinery: a synthesized `__GenCtx` context object; `yield` publishes the value and parks until the next `next()` (`Tests/AsyncTest/YieldIterateTest.md`).

## Emission

`LLVMEmitter.penguin`: `needs_sched_run` (line 311); in suspend mode `emit_main` spawns each initial with `call void @_emperor_co_spawn_fn0(ptr @initial)` (lines 1744–1755) then calls `@__builtin___sched_run()` (lines 1766–1775). `std/penguin/scheduler.penguin` is auto-loaded with `--enable-coroutine` (line 1150).

## The Native Runtime — Two Layers

**`std/c/scheduler.c`** (3164 lines) — the C microkernel:

- **Stackful coroutines** via POSIX `swapcontext`/`makecontext` (line 688) or Windows `CreateFiberEx` (line 701). Each coroutine owns a stack; switching is a context swap, so a `wait` anywhere in a C call tree parks the whole coroutine.
- Spawn inbox FIFO (adopted per thread at line 2464); `_co_switch_in` (line 790) resume with a GC protocol handshake; `_co_fingerprint` (line 1334) = a hash of a coroutine's frozen stack (used by the policy layer's quiescence detection).
- The **MT execution kernel** — the run queues, park filing, wake routing, timer table and future-waiter set the mt/free schedulers drive, plus the M4b worker threads — also lives in this file; the scheduler sections below cover it.

**`std/penguin/scheduler.penguin`** (~1586 lines) — the policy layer written in PenguinLang, mirroring BabyPenguin's SimScheduler 1:1: ready/next-delta queues, `_sched_timers` + `_timer_at`/`_timers_fire`, `_sim_now`/`_sim_delta`/`_sim_activity`/`_sim_settled`, quiescence by combined per-coroutine fingerprints over two identical idle rounds (`__sched_run` line 555+; header comment: "Structurally 1:1 with the old C loop"), and fd waiters (`_fd_wait_read`/`_fd_wait_write`) blocking in `poll()` at quiescence instead of exiting — the mechanism the stdio-based LSP server relies on.

The split mirrors BabyPenguin: the microkernel owns stacks and switching (what C must do); the scheduler policy (deltas, timers, quiescence) is Penguin-level library code, kept testable and identical between the two runtimes by the cross-compiler async/timing test categories (`Tests/AsyncTest/`, `Tests/TimingTest/`, `Tests/PortTest/`, `Tests/ChannelTest/` — all run on BabyPenguin and Pass3 with byte-identical expectations).

## The Clock Model (`--enable-multithread-scheduler --enable-clock-model`)

`main.penguin` swaps the auto-loaded policy: `std/penguin/scheduler_mt.penguin` replaces `scheduler.penguin` (same public surface, same namespace, so the emitter is unchanged). The convergence-delta semantics — full convergence per delta, the seven-step boundary, exit only when every coroutine finished — are specified in `docs/zh/wip/concurrent_scheduler.md` (the design document; EN summary pending). `wait <n>` lowers to `__builtin._mt_wait_ticks(n)` (`SemanticBindExpressions.bind_timer_wait`): `n < 0` is a pure yield (`_co_yield`, stays runnable in the current delta), `n == 0` parks through `_co_wait()` exactly like a bare `wait;` (the timer path cannot express it — `_after(0)` registers a deadline equal to now, which `do_wait`'s poll-first fast path would deliver in the same delta as a no-op; pinned by `Tests/MtSchedulerTest/MtWaitZeroEqualsBare.md`), and `n > 0` is the timer condition wait.

Pass 10 (confinement analysis, see [Bound Model](./26_EmperorPenguinBound.md)) runs under the multi-thread scheduler (both policies): it marks stealable `async` spawn sites so worker threads can distribute them; `-Wconcurrency` prints per-spawn-site stealable/pinned diagnostics with the pin reason chain.

### Worker threads (`EMPEROR_THREADS=N`)

`EMPEROR_THREADS=N` (env, default 1) starts N−1 worker threads; a delta runs in parallel on all N — each thread drains its own queue, and Pass-10-stealable never-run coroutines are distributed round-robin at delta start and stealable by idle threads. The delta ends at a global barrier: every thread simultaneously out of work. Decision A (started coroutines never migrate) makes wake routing race-free — a wake goes to the coroutine's owner, which by definition is not executing it. With `EMPEROR_THREADS` unset the single worker is the calling thread, byte-identical to the single-worker kernel.

### Per-thread time analysis (`EP_MULTITHREAD_SCHEDULER_PERF_ANALYZE=1`)

`=1` splits every scheduler thread's wall time into four phases and prints the per-thread report to stderr at program exit (after the workers join):

- **busy** — inside `_emperor_co_switch_in`: running penguin coroutine code.
- **idle** — parked waiting for work (`mt_idle_wait` sleep + the between-delta park; the pre-futex spin windows count as idle).
- **schedule** — scheduler bookkeeping: run queues, wake routing, stealing, the boundary.
- **gc** — inside a GC driver (gc.c calls the scheduler's phase hooks around its collect funnels).

Times are per thread in µs with the percentage of that thread's own measured total, plus a `total:` line; classic-mode collections (no mt slot) and pre-scheduler time are not accounted. Cost when unset: one relaxed load + compare per phase switch. Pinned by `Tests/FreeMtTest/FreeMtPerfAnalyzeReport.md`.

### Stall Diagnostics (design doc ch. 12: never a silent hang)

Two stall classes are diagnosed by leash budgets in `std/c/scheduler.c` instead of hanging forever:

| Class | Detected as | Env knob (default) | Exit code |
|---|---|---|---|
| (a) a delta that never converges — a wake-livelock or a `while (!cond) wait -1;` spin | resumes in the current delta exceed the step budget (`mt_run_one` counts, `_emperor_mt_run_delta` resets per delta) | `EMPEROR_SCHED_STEP_BUDGET` (20,000,000; 0 disables) | 60 |
| (b) the clock is pinned — deltas keep converging while time-waiters sit pending and no timer ever fires; classically `while (!cond) wait;` (and `wait 0;` spins alike) | consecutive converged deltas with pending-but-unfired timers (`_emperor_mt_fire_due` resets the streak only when a timer fires at a **strictly later** deadline — real clock progress, so a `wait 0` re-registration does not count) | `EMPEROR_SCHED_PIN_BUDGET` (20,000,000; 0 disables) | 61 |

Both print one diagnostic to stderr (`emperor sched: stall (exit N): …` with the suspected source pattern and live-coroutine count) and stop the program through the same path as `exit()`. They are leashes, not detectors: a legitimate long delta (deep immediate-container pipeline) or a long same-tick churn (a big deferred pipeline) stays orders below the defaults — `Tests/MtSchedulerTest/MtStallBudgetsQuiet.md` runs with tight budgets exceeding its totals to pin the reset logic. A pure `while (true) {}` without a yield point is *not* caught (no resume ever returns to the scheduler) — same as today. Tests: `MtStallLivelockDiag` (60), `MtStallClockPinnedDiag` (61).

The decision-E hang (exit only when every coroutine finished; a parked coroutine with no wake source blocks that forever) gets its own reporter: a converged delta that ran nothing, with no owed parkers, timers or fd waiters, while coroutines are still live, means a wake was lost. `EMPEROR_IDLE_DUMP=N` (default 1000 rounds, 0 disables) dumps every live coroutine's park filing to stderr and stops with exit 63 instead of hanging silently.

### The Allocation Hard Cap Pressure Valve (design doc §10.1, M5 v0)

A delta can be arbitrarily long while collection happens only at its stop-the-world boundary — under the mt kernel (polls defer to the boundary inside the parallel window) a coroutine allocating in a tight loop would grow the heap without bound. The allocator carries a hard cap (`std/c/gc.c`): when the next allocation's charge would cross it, the touching coroutine parks on "wait for GC" (`_emperor_sched_alloc_park`, CO_COND state, **not runnable**); an all-blocked delta therefore converges into the boundary's stop-the-world collection, which frees the garbage, releases the parkers and lets the allocation retry — "everything blocked" means "collect now", never deadlock or OOM.

- Knob: `EMPEROR_GC_HARD_CAP` (bytes; default 8 GiB — far above any real workload (self-compile peaks near 0.6 GiB) while runaway allocation storms park instead of eating the machine; explicit `0` = unlimited).
- **Deterministic OOM (exit code 62), not a leash**: after the boundary's collection, if the live set still cannot admit the *smallest pending charge* (`_emperor_gc_cap_blocks`; the park list tracks the minimum), no collection can ever unblock anyone — report and stop via the `exit()` channel. The charge must be part of the predicate: `live > cap` alone is not enough — the parkers stall in the gap between "live" and "live + charge" (measured: a keep-live loop parked forever at exactly cap − 48 with every collection freeing nothing). A single allocation larger than the cap is the same rule degenerated, exiting 62 immediately.
- Scope: the valve parks only under the mt kernel; on the classic scheduler, main-stack policy code and the sequential fallback the allocation proceeds (the cap is advisory there — growth stays bounded by the existing inline emergency collect and the goal cadence, byte-identical to before the valve; `MtGcHardCapClassicAdvisory` pins that). The trip check sits outside the allocator lock (concurrent overshoot = one in-flight charge per thread — acceptable slack for a valve).
- Drive-by fix: the 8×-goal emergency collect no longer runs inline inside the parallel window (inline marking would race the worker mutators) — it defers to the boundary, and the hard cap is what bounds the window's growth.
- Poll-side STW escalation (`EMPEROR_GC_STW_MS`, default 250 ms, 0 = off): a want_collect that the boundary cannot drain — the delta never converges (a busy service churning through a perpetual delta) — escalates: the polling coroutine files onto the hard-cap valve's parked list, forcing the convergence the boundary needs to collect. Without it a perpetual delta holds collection off until the heap climbs to the hard cap — sound, but the heap sits at the cap instead of the goal. The age clock starts at the first poll that observes the flag and re-arms whenever the flag is down.
- Tests: `MtGcHardCapParkCollect` (four parallel churns pass through a 256KB cap and complete, checksum unchanged), `MtGcHardCapOom` (keep-live 62), `MtGcHardCapSingleAllocOom` (single oversize allocation 62), `MtGcHardCapZeroUnlimited` (explicit 0 = unlimited).

## The Free-form Runtime (`--enable-multithread-scheduler`)

`main.penguin` swaps the policy again: `std/penguin/scheduler_free.penguin` — the same C microkernel and the same public surface as `scheduler_mt.penguin`, for services that use no simulation time:

- **No virtual clock**: there is no tick to advance and no timer table; `wait n;` with n > 0 is rejected at run time (there is no tick to wait on — wait on an Event, a future or an AsyncQueue instead); `wait;` / `wait 0;` park one convergence round and negative `wait` is a pure yield, exactly as in sim-mt.
- **Immediate components**: Signal/Fifo/LatestChannel/Event commit and wake inline — a write's effect is visible to a woken reader in the SAME convergence round (the deferred-commit layer of sim-mt is inverted; the boundary has nothing to commit).
- **Cross-thread structure** goes through `AsyncQueue<T>`: an MPMC queue guarded by an AtomicBool spinlock (a failed test-and-set backs off with a pure yield — the lock is never held across a park); takers finding it empty park as condition waiters and are woken by enqueue. It implements `ISync`, so coroutines holding one are Pass-10 stealable and genuinely run on different threads under `EMPEROR_THREADS`; the immediate Signal/Fifo/Latest/Event stay single-domain (Pass 10 pins their holders, same lattice as sim-mt).
- The boundary keeps only: owed/runnable check → non-blocking fd probe → blocking fd poll when only fd waiters remain → exit when every coroutine finished, otherwise hang by design (decision E; `EMPEROR_IDLE_DUMP` reports it).

For programs that use no simulation time, free-mt and sim-mt behave identically — free-mt exists so services never pay for a virtual clock, and so `wait n` fails loudly instead of silently scheduling on a tick that never advances. Tests: `Tests/FreeMtTest/` (Pass3 only).

## GC Interaction

Coroutine stacks are enumerated by the green-tea collector through the frame-chain mechanism (see [EmperorPenguin GC](./29_EmperorPenguinGC.md)): each parked coroutine's frame chain is a precise root set, and the conservative stack cover applies to the active stack only. `_co_switch_in` coordinates with the GC so a collection never observes a half-switched stack.
