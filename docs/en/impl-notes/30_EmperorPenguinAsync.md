# 30. EmperorPenguin Async

EmperorPenguin implements the async/wait language surface (see the [Async & Timing Model spec](../specifications/09_AsyncAndTimingModel.md)) with **binder desugaring plus stackful coroutines (fibers)** — not LLVM state machines. This page documents the gate, the lowerings, and the two-layer native runtime.

## The Coroutine Gate

`enable_coroutine: bool = false` (`EmperorPenguin/src/bound/EmperorPenguinCompiler.penguin` lines 49–52) gates ports, `construct`, `connect`, every `wait` form, try/catch, and `async`. Any of them without the flag reports `E_UNSUPPORTED "requires the --enable-coroutine option"` through `require_coroutine` (`src/bound/SemanticModel.penguin` line 423) — verified by `Tests/PortTest/CoroutineSyntaxRequiresFlag.md`. BabyPenguin implements the same features unconditionally. Every wait/async also sets `unit.has_suspension = true` (`src/bound/BoundCompilationUnit.penguin` lines 28–31) — a wait-free program emits a plain sequential `main` regardless of the flag (LLVMEmitter.penguin lines 779–780).

## wait Lowering (SemanticBindExpressions.penguin)

`bind_wait_expr` (line 414+) desugars every wait form into ordinary control flow over two runtime primitives — `_co_wait()` (park one delta) and `IFuture.do_wait()`:

| Source form | Desugaring |
|---|---|
| `wait;` | `__builtin._co_wait()` |
| `wait n tick;` / `wait <int expr>;` | `_after(n).do_wait()` |
| `wait change(x)` | `{ let __wait_sample = x; while (x == __wait_sample) { _co_wait(); } x }` |
| `wait <bool condition>;` | `while (!cond) { _co_wait(); }` |
| `wait ev` (`Event<T>`) | `cast<mut IFuture<T>>(new _EventSubscription<T>(ev)).do_wait()` |
| `wait <IFuture>` / `wait <port>` | cast + `.do_wait()` (`mk_do_wait_call`, lines 134–143) |

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

**`std/c/scheduler.c`** (~930 lines) — the C microkernel:

- **Stackful coroutines** via POSIX `swapcontext`/`makecontext` (lines 234–252, 329–363) or Windows `CreateFiberEx` (lines 309, 376). Each coroutine owns a stack; switching is a context swap, so a `wait` anywhere in a C call tree parks the whole coroutine.
- Spawn inbox FIFO (lines 202–232); `_co_switch_in` resume with a GC protocol handshake; `_co_fingerprint` = a hash of a coroutine's frozen stack (used by the policy layer's quiescence detection).

**`std/penguin/scheduler.penguin`** (~1578 lines) — the policy layer written in PenguinLang, mirroring BabyPenguin's SimScheduler 1:1: ready/next-delta queues, `_sched_timers` + `_timer_at`/`_timers_fire`, `_sim_now`/`_sim_delta`/`_sim_activity`/`_sim_settled`, quiescence by combined per-coroutine fingerprints over two identical idle rounds (`__sched_run` line 555+; header comment: "Structurally 1:1 with the old C loop"), and fd waiters (`_fd_wait_read`/`_fd_wait_write`) blocking in `poll()` at quiescence instead of exiting — the mechanism the stdio-based LSP server relies on.

The split mirrors BabyPenguin: the microkernel owns stacks and switching (what C must do); the scheduler policy (deltas, timers, quiescence) is Penguin-level library code, kept testable and identical between the two runtimes by the cross-compiler async/timing test categories (`Tests/AsyncTest/`, `Tests/TimingTest/`, `Tests/PortTest/`, `Tests/ChannelTest/` — all run on BabyPenguin and Pass3 with byte-identical expectations).

## GC Interaction

Coroutine stacks are enumerated by the green-tea collector through the frame-chain mechanism (see [EmperorPenguin GC](./29_EmperorPenguinGC.md)): each parked coroutine's frame chain is a precise root set, and the conservative stack cover applies to the active stack only. `_co_switch_in` coordinates with the GC so a collection never observes a half-switched stack.
