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

**`SimScheduler.cs`** (415 lines, singleton) — a deterministic, discrete-tick, delta-round cooperative scheduler:

- `Run()` (line 74) loop:
  1. Drain the Penguin-level `__builtin._main_scheduler.pending_jobs` queue (reached reflectively through the object graph, `GetPendingJobItems` line 340) into a C# ready queue.
  2. **Delta round**: run every ready job (`RunJob` line 278 invokes the `RoutineContext.call` extern; a Blocked status re-enqueues; status 3/4 = done).
  3. `FireTimers()` (line 321): fire timers whose deadline ≤ the current tick.
  4. If no progress and no activity (`RuntimeGlobal.SimActivityCounter`) but timers remain: **advance `_currentTick` to the earliest deadline** (lines 160–167) and fire.
  5. **Quiescence**: fingerprint every parked job (RefId + suspended frame snapshot, lines 202–234); two consecutive identical signal-free rounds → the program ends normally (exit 0). Round budget 20,000,000 → "SimScheduler exceeded max rounds (possible live-lock)" (lines 89, 112). There is no external event source in v1, so a parked-forever program is indistinguishable from quiescence (comment lines 169–177).
- Timers: `__builtin._after(n)` (ExternFunctions.cs line 780) allocates a `_TimerWait` future and registers it with `EnqueueTimerFuture(deadlineTick, future)`.
- Timing externs (ExternFunctions.cs): `_sim_now` (756), `_sim_delta` (763), `_sim_settled` (772), `_sim_activity` (82), `_after` (780), `_run` (825 — drives `SimScheduler.Run`, converts `Exited` into `ProgramExitException`).

The coroutine runtime itself is **Penguin-level code** in `BabyPenguin/Builtin.penguin`: `Scheduler` (line 717, `pending_jobs : mut _utils.Queue<IFutureBase>`), `RoutineContext` (line 755; its `.call()` extern re-enters the suspended `RuntimeFrame` — C# side `RoutineContextCall`, ExternFunctions.cs lines 308–374), `IFuture`/`IFutureBase` (659–669), `_TimerWait`, `Event<T>` + channels (see [Async & Timing Model spec](../specifications/09_AsyncAndTimingModel.md) for the language semantics). This mirrors the native runtime's split (scheduler.c microkernel + scheduler.penguin policy — see [EmperorPenguin Async](./30_EmperorPenguinAsync.md)).

## ExternFunctions — the C# Builtin Surface

**`ExternFunctions.cs`** (932 lines), registered by `Build` (lines 15–32), all namespaced `__builtin.` / `_utils.` to match declarations in Builtin.penguin/Utils.penguin:

- I/O: print/println/eprint/eprintln/exit/`__throw_runtime_error`.
- `ICopy<T>.copy`; `AtomicI64` swap/compare_exchange/fetch_add.
- `List<T>` new/at/push/pop/remove/size/set; `Queue<T>` enqueue/dequeue/peek/size; StringBuilder new/append/to_string.
- `_utils` file/process/env: file_read_text/write_text/size/read_range/append/exe_path/getenv/mkdir/file_exists/dir_exists/dir_get_entries/create_temp_dir; `__builtin._exec_cmd` (sh -c / cmd.exe); `__args_count/__args_get`.
- `__builtin.string_*`: length/find/find_from/substring/char_at/char_code(_at)/starts_with_at/slice/to_int/to_double; lshift/rshift.
- Scheduler/timing: `_sim_*`, `_after`, `_run` (above).
