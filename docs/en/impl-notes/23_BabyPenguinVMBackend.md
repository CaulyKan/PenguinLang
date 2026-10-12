# 23. BabyPenguin VM Backend

The BabyPenguin VM is the reference executor: a register-based interpreter over the shared IR (see [PenguinLang IR](./22_PenguinLangIR.md)), with coroutine suspension, a tick/delta scheduler, and a mark-sweep garbage collector. All under `BabyPenguin/VirtualMachine/`.

## BabyPenguinVM

**`BabyPenguinVM.cs`** (451 lines):

- `BabyPenguinVM(SemanticModel)` (line 9): materializes global-variable `IRuntimeSymbol`s into `RuntimeGlobal.GlobalVariables`; `ExternFunctions.Build(this)` registers the C# builtins.
- `Initialize()` (line 29): `IRGenerator(Model).Generate()` → `Global.IRModule`; builds `CodeContainerIndex` (sanitized name → ICodeContainer) and `SanitizedExternFunctionIndex` (O(1) lookups — profiling showed 38% of runtime in name lookups); resolves `__builtin._main` and creates the start `RuntimeFrame`.
- `Run()` (line 69): `Model.EnableResolutionCache()`; **fast path** `StartFrame.RunDirect()` when no breakpoints and StepMode == Run; otherwise the **iterator path** (`Run()`) for DAP debugging (step modes need per-instruction yields). `ProgramExitException` → `Global.ExitCode`.
- `RuntimeGlobal` (line 135):
  - `Dictionary<ulong, ReferenceRuntimeValue> AllObjects` (line 139) + `NextRefId()`; object pool (recycled `ReferenceRuntimeValue`s, cap 100k).
  - **Mark-sweep GC** `CollectGarbage` (line 233): roots = global variables + SimScheduler timer futures & C# ready-queue jobs + frame-chain registers; adaptive threshold 1.5× live set (min 50k); every 10 cycles forces .NET Gen2 + LOH compaction.
  - `SimActivityCounter` (line 371, liveness signal for the scheduler), `ExitCode`, `CommandLineArgs`, `StepModeEnum {StepIn, StepOver, StepOut, Run}`, `Breakpoints`, `MethodDispatchCache` (memoized virtual dispatch), `Output`/`PrintFunc` (output captured and optionally echoed), `RegisterExternFunction` overloads (407–419).
  - `BabyPenguinRuntimeException` (line 120, carries `ErrorCode` + a program-raised-panic flag).

## RuntimeFrame — the Interpreter

**`RuntimeFrame.cs`** (2369 lines):

- Records: `RuntimeFrameResult(IRuntimeSymbol? ReturnValue, ReturnStatus ReturnStatus)` (line 6); `RuntimeBreak(RuntimeBreakReason, RuntimeFrame)` (line 8).
- `RuntimeFrame` (line 10) holds an `IRFunction`, the register array `IRuntimeValue[RegisterCount]`, a cached label map, `_ip`, `_pendingCallResult`, and `ChildFrame` (a suspended coroutine). The constructor (line 52) looks up the IR function by sanitized container name.
- **Two execution engines**:
  - `Run()` (line 132): a C# iterator `IEnumerable<Or<RuntimeBreak, RuntimeFrameResult>>` — per-instruction `StepOneInstruction` buffers yields into an `IteratorStepBuffer` (needed because `yield return` cannot appear inside try/catch). Supports StepIn/StepOver DAP breaks, GC checks, and `StepResumeChild` (line 186) to resume a blocked child frame.
  - `RunDirect()` (line 804) → `RunDirectCore()` (line 858): a plain loop with no iterator state machines — the normal path.
- Value semantics: the `this` receiver argument is by-reference; other value-type parameters are copied on entry (the `ARG` handler, lines 258–268 / 886–894). Reads copy value types unless flagged `IsWriteChain`/`IsAliasChain` (lvalue addressing for write chains, lines 304–314).
- Exceptions: `TryDispatchCatch` (line 826) matches `_function.CatchRegions` (innermost-first; `IRFunction.CatchRegion(StartIP, EndIP, HandlerIP, CatchRegister)`, IRFunction.cs line 35), stores a `__builtin.RuntimeError` object (`CreateRuntimeErrorObject`, line 846), and jumps to the handler.
- Coroutine suspension: the `ReturnStatus` on RET (`Blocked`/`YieldNotFinished`/`Finished`/`YieldFinished`, defined in `BabyPenguinIR.cs` lines 42–48) — a Blocked/Yield return saves `_ip+1` and parks the frame via `ChildFrame`; the parent re-enters through `StepResumeChild`.
- Not implemented in the interpreter: `IRBoxInst`, `IRUnboxInst`, `IRCallVirtInst` (lines 786–789 throw `NotImplementedException`) — interface dispatch goes through funptr `RDMBR` + `CALL_FUNC_PTR` instead.

## Runtime Values

**`RuntimeValue.cs`** (425 lines):

- `IRuntimeValue { TypeInfo, Clone() }`; `NotInitializedRuntimeValue`.
- `BasicRuntimeValue` (line 56): primitives backed by `[StructLayout(LayoutKind.Explicit)] BasicValueUnion` — an 8-byte union of bool/u8..u64/i8..i64/float/double/char (line 40) plus a separate string ref; `DynamicValue` getter/setter.
- `FunctionRuntimeValue` (line 179): a `FunctionSymbol` + `Owner` (fat-pointer receiver; the Owner is attached when a method is read off an object, RuntimeFrame.cs line 309).
- `ReferenceRuntimeValue` (line 244): `Fields` dictionary + `RefId`, **registered in `RuntimeGlobal.AllObjects` at construction**; `ExternImplenmentationValue` (the `__extern_impl` field) stores native C# backing (List, Queue, StringBuilder, a suspended RuntimeFrame, …); supports pooling via `Reuse`.
- `EnumRuntimeValue` (line 327): `FieldsValue` (a ReferenceRuntimeValue whose `_value` int is the variant tag) + `ContainingValue` payload.
- `ExternRuntimeValue` (225), `RuntimeValueCopier` (384 — deep value-semantics copy).
- **`RuntimeSymbol.cs`** (370): `IRuntimeSymbol.FromSymbol` factory (line 29) dispatches to BasicRuntimeSymbol / FunctionRuntimeSymbol / ClassRuntimeSymbol (pre-seeds interface member fields from VTables) / InterfaceRuntimeSymbol / EnumRuntimeSymbol; plus `SimpleRuntimeSymbol` (RuntimeFrame.cs line 2347) for call results.

## Concurrency: SimScheduler

**`SimScheduler.cs`** (612 lines, singleton) — the C# reference implementation of the M0 scheduler semantics: fully-converging delta ticks, the seven-step boundary, the all-finished exit rule, tick-pinning bare waits and pure-yield negative waits. The native mt kernel (`std/c/scheduler.c` + `scheduler_mt.penguin`, see [EmperorPenguin Async](./30_EmperorPenguinAsync.md)) implements the same semantics; the cross-compiler test categories (`Tests/MtSchedulerTest/`, `Tests/TimingTest/`) pin both.

- A **delta** (`Run`, line 187): everything runnable runs until none is — spawns adopted mid-delta run in their spawning delta (`AdoptSpawns` line 361), a pure yield re-queues at the tail of the same delta, a kicked condition parker resumes in the kicking delta. Each resume is one step of the current delta (`RunOneDeltaJob` line 394): status 3/4 = finished, status 2 = a generator yielded a value (deferred kick — consumers re-poll in the next delta), status 0 = a park, filed by the side-channel park note.
- **Park filing** (`SchedParkNote`): the VM's park instruction is a plain Blocked return, so the Penguin-level helper immediately preceding the `wait` sets a scheduler note — `PureYield` (`wait -1`: re-queue, still runnable), `FuturePark` (do_wait/channel backpressure: owed nothing, woken only by a kick), none (bare `wait;`/`wait 0;`: owed the next delta at the CURRENT tick).
- The **boundary** (seven steps, `Run` lines 254–325): deferred kicks (the channel family commits immediately; only the WAKE defers — a transaction that flowed this delta wakes its waiters in the next delta, `KickFutures` line 172) → fire due timers (`FireDueTimers` line 499; a firing at a strictly later deadline resets the pin streak) → owed bare-wait parkers or anyone runnable means the next delta at the SAME tick (the clock may not jump past an owed parker) → no fd sources in the C# VM → advance the tick to the earliest future deadline → exit only when every coroutine finished (`_liveJobs` empty); parked-with-no-wake-source is NOT an exit (decision E — the loop sleeps quietly instead).
- **Stall leashes** (mirror the native kernel, `ParseBudgets` line 327): `EMPEROR_SCHED_STEP_BUDGET` (default 20,000,000, 0 disables; exit 60 — resumes in one delta exceeded, a zero-time loop or livelock) and `EMPEROR_SCHED_PIN_BUDGET` (default 20,000,000, 0 disables; exit 61 — deltas keep converging while timer waiters sit pending and no timer fires at a strictly later deadline, classically `while (!cond) wait;`). Both print one diagnostic and stop the program (`StallAbort` line 343).
- Timers: `__builtin._after(n)` (ExternFunctions.cs line 780) allocates a `_TimerWait` future and registers it with `EnqueueTimerFuture(deadlineTick, future)` (line 156) against the current job.
- Timing externs (ExternFunctions.cs): `_sim_now` (756), `_sim_delta` (763 — the monotonic delta-round counter, wires merge same-round writes), `_sim_settled` (772 — true when the previous completed round carried no transaction activity and none has happened in the current round so far; settle-point port reads park until it holds), `_sim_activity` (82), `_after` (780), `_run` (854 — drives `SimScheduler.Run`, converts `Exited` into `ProgramExitException`).
- Park/kick externs: `_sched_yield` (829), `_sched_future_park` (834), `_sched_defer_kick` (841 — a transaction that waiters observe in the NEXT delta), `_sched_kick_futures` (848 — immediate wake of every condition parker).

The coroutine runtime itself is **Penguin-level code** in `BabyPenguin/Builtin.penguin`: `Scheduler` (line 739, `pending_jobs : mut _utils.Queue<IFutureBase>`), `RoutineContext` (line 777; its `.call()` extern re-enters the suspended `RuntimeFrame` — C# side `RoutineContextCall`, ExternFunctions.cs lines 308–374), `IFuture`/`IFutureBase` (659–669), `_TimerWait` (960), `Event<T>` (863) + the channel family (`Fifo` 1095, `LatestChannel` 1240, `MergeChannel` 1414 — every commit path routes through `_sched_defer_kick`; see [Async & Timing Model spec](../specifications/09_AsyncAndTimingModel.md) for the language semantics). This mirrors the native runtime's split (scheduler.c microkernel + scheduler.penguin policy — see [EmperorPenguin Async](./30_EmperorPenguinAsync.md)).

## ExternFunctions — the C# Builtin Surface

**`ExternFunctions.cs`** (961 lines), registered by `Build` (lines 15–32), all namespaced `__builtin.` / `_utils.` to match declarations in Builtin.penguin/Utils.penguin:

- I/O: print/println/eprint/eprintln/exit/`__throw_runtime_error`.
- `ICopy<T>.copy`; `AtomicI64` swap/compare_exchange/fetch_add.
- `List<T>` new/at/push/pop/remove/size/set; `Queue<T>` enqueue/dequeue/peek/size; StringBuilder new/append/to_string.
- `_utils` file/process/env: file_read_text/write_text/size/read_range/append/exe_path/getenv/mkdir/file_exists/dir_exists/dir_get_entries/create_temp_dir; `__builtin._exec_cmd` (sh -c / cmd.exe); `__args_count/__args_get`.
- `__builtin.string_*`: length/find/find_from/substring/char_at/char_code(_at)/starts_with_at/slice/to_int/to_double; lshift/rshift.
- Scheduler/timing: `_sim_*`, `_after`, `_sched_yield`/`_sched_future_park`/`_sched_defer_kick`/`_sched_kick_futures`, `_run` (above).
