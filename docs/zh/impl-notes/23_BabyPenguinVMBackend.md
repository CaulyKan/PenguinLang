# 23. BabyPenguin VM 后端

BabyPenguin VM 是参考执行器：基于共享 IR（见 [PenguinLang IR](./22_PenguinLangIR.md)）的寄存器型解释器，带协程挂起、tick/delta 调度器与标记-清扫垃圾回收。全部位于 `BabyPenguin/VirtualMachine/`。

## BabyPenguinVM

**`BabyPenguinVM.cs`**（451 行）：

- `BabyPenguinVM(SemanticModel)`（9 行）：把全局变量 `IRuntimeSymbol` 物化进 `RuntimeGlobal.GlobalVariables`；`ExternFunctions.Build(this)` 注册 C# 内建。
- `Initialize()`（29 行）：`IRGenerator(Model).Generate()` → `Global.IRModule`；构建 `CodeContainerIndex`（净化名 → ICodeContainer）与 `SanitizedExternFunctionIndex`（O(1) 查找——性能剖析显示 38% 运行时耗在名字查找）；解析 `__builtin._main` 并创建起始 `RuntimeFrame`。
- `Run()`（69 行）：`Model.EnableResolutionCache()`；无断点且 StepMode == Run 时走**快速路径** `StartFrame.RunDirect()`；否则走**迭代器路径**（`Run()`）支持 DAP 调试（步进模式需要逐指令 yield）。`ProgramExitException` → `Global.ExitCode`。
- `RuntimeGlobal`（135 行）：
  - `Dictionary<ulong, ReferenceRuntimeValue> AllObjects`（139 行）+ `NextRefId()`；对象池（回收 `ReferenceRuntimeValue`，上限 10 万）。
  - **标记-清扫 GC** `CollectGarbage`（233 行）：根 = 全局变量 + SimScheduler 定时 future 与 C# 就绪队列作业 + 帧链寄存器；自适应阈值 1.5× 存活集（最小 5 万）；每 10 轮强制 .NET Gen2 + LOH 压缩。
  - `SimActivityCounter`（371 行，调度器的活性信号）、`ExitCode`、`CommandLineArgs`、`StepModeEnum {StepIn, StepOver, StepOut, Run}`、`Breakpoints`、`MethodDispatchCache`（记忆化虚分派）、`Output`/`PrintFunc`（输出捕获并可选回显）、`RegisterExternFunction` 重载（407–419）。
  - `BabyPenguinRuntimeException`（120 行，携带 `ErrorCode` 与程序 panic 标志）。

## RuntimeFrame——解释器

**`RuntimeFrame.cs`**（2369 行）：

- 记录：`RuntimeFrameResult(IRuntimeSymbol? ReturnValue, ReturnStatus ReturnStatus)`（6 行）；`RuntimeBreak(RuntimeBreakReason, RuntimeFrame)`（8 行）。
- `RuntimeFrame`（10 行）持有 `IRFunction`、寄存器数组 `IRuntimeValue[RegisterCount]`、缓存标签表、`_ip`、`_pendingCallResult` 与 `ChildFrame`（被挂起协程）。构造器（52 行）按净化容器名查 IR 函数。
- **两个执行引擎**：
  - `Run()`（132 行）：C# 迭代器 `IEnumerable<Or<RuntimeBreak, RuntimeFrameResult>>`——逐指令 `StepOneInstruction` 把 yield 缓冲进 `IteratorStepBuffer`（因为 `yield return` 不能出现在 try/catch 内）。支持 StepIn/StepOver DAP 断点、GC 检查与 `StepResumeChild`（186 行）恢复被阻塞子帧。
  - `RunDirect()`（804 行）→ `RunDirectCore()`（858 行）：无迭代器状态机的普通循环——常规路径。
- 值语义：`this` 接收者参数按引用传递；其他值类型参数进入时复制（`ARG` 处理器，258–268 / 886–894 行）。读取时值类型复制，除非带 `IsWriteChain`/`IsAliasChain` 标志（写链的左值寻址，304–314 行）。
- 异常：`TryDispatchCatch`（826 行）匹配 `_function.CatchRegions`（最内优先；`IRFunction.CatchRegion(StartIP, EndIP, HandlerIP, CatchRegister)`，IRFunction.cs 35 行），存入 `__builtin.RuntimeError` 对象（`CreateRuntimeErrorObject`，846 行）并跳到处理器。
- 协程挂起：RET 上的 `ReturnStatus`（`Blocked`/`YieldNotFinished`/`Finished`/`YieldFinished`，定义在 `BabyPenguinIR.cs` 42–48 行）——Blocked/Yield 返回保存 `_ip+1` 并经 `ChildFrame` 停靠帧；父级经 `StepResumeChild` 重入。
- 解释器未实现：`IRBoxInst`、`IRUnboxInst`、`IRCallVirtInst`（786–789 行抛 `NotImplementedException`）——接口分派改走函数指针 `RDMBR` + `CALL_FUNC_PTR`。

## 运行时值

**`RuntimeValue.cs`**（425 行）：

- `IRuntimeValue { TypeInfo, Clone() }`；`NotInitializedRuntimeValue`。
- `BasicRuntimeValue`（56 行）：基元以 `[StructLayout(LayoutKind.Explicit)] BasicValueUnion` 承载——bool/u8..u64/i8..i64/float/double/char 的 8 字节联合（40 行）加独立 string 引用；`DynamicValue` getter/setter。
- `FunctionRuntimeValue`（179 行）：`FunctionSymbol` + `Owner`（胖指针接收者；从对象读方法时附加 Owner，RuntimeFrame.cs 309 行）。
- `ReferenceRuntimeValue`（244 行）：`Fields` 字典 + `RefId`，**构造时注册进 `RuntimeGlobal.AllObjects`**；`ExternImplenmentationValue`（`__extern_impl` 字段）保存原生 C# 后备（List、Queue、StringBuilder、被挂起 RuntimeFrame……）；支持经 `Reuse` 入池。
- `EnumRuntimeValue`（327 行）：`FieldsValue`（`_value` int 为变体标签的 ReferenceRuntimeValue）+ `ContainingValue` 载荷。
- `ExternRuntimeValue`（225）、`RuntimeValueCopier`（384——深值语义复制）。
- **`RuntimeSymbol.cs`**（370）：`IRuntimeSymbol.FromSymbol` 工厂（29 行）分派到 BasicRuntimeSymbol / FunctionRuntimeSymbol / ClassRuntimeSymbol（从虚表预播种接口成员字段）/ InterfaceRuntimeSymbol / EnumRuntimeSymbol；另有调用结果的 `SimpleRuntimeSymbol`（RuntimeFrame.cs 2347 行）。

## 并发：SimScheduler

**`SimScheduler.cs`**（415 行，单例）——确定性、离散 tick、delta 回合的协作调度器：

- `Run()`（74 行）循环：
  1. 把 Penguin 层的 `__builtin._main_scheduler.pending_jobs` 队列（经对象图反射到达，`GetPendingJobItems` 340 行）抽干到 C# 就绪队列。
  2. **Delta 回合**：运行每个就绪作业（`RunJob` 278 行调用 `RoutineContext.call` extern；Blocked 状态重新入队；状态 3/4 = 完成）。
  3. `FireTimers()`（321 行）：触发截止 ≤ 当前 tick 的定时器。
  4. 若无进展且无活动（`RuntimeGlobal.SimActivityCounter`）但仍有定时器：**把 `_currentTick` 推进到最早截止**（160–167 行）并触发。
  5. **静默**：对每个停靠作业取指纹（RefId + 被挂起帧快照，202–234 行）；连续两轮相同且无信号 → 程序正常结束（退出码 0）。回合预算 2000 万 → "SimScheduler exceeded max rounds (possible live-lock)"（89、112 行）。v1 没有外部事件源，永久停靠的程序与静默不可区分（注释 169–177 行）。
- 定时器：`__builtin._after(n)`（ExternFunctions.cs 780 行）分配 `_TimerWait` future 并经 `EnqueueTimerFuture(deadlineTick, future)` 注册。
- 时序 extern（ExternFunctions.cs）：`_sim_now`（756）、`_sim_delta`（763）、`_sim_settled`（772）、`_sim_activity`（82）、`_after`（780）、`_run`（825——驱动 `SimScheduler.Run`，把 `Exited` 转为 `ProgramExitException`）。

协程运行时本体是 `BabyPenguin/Builtin.penguin` 中的 **Penguin 层代码**：`Scheduler`（717 行，`pending_jobs : mut _utils.Queue<IFutureBase>`）、`RoutineContext`（755 行；其 `.call()` extern 重入被挂起的 `RuntimeFrame`——C# 侧 `RoutineContextCall`，ExternFunctions.cs 308–374 行）、`IFuture`/`IFutureBase`（659–669）、`_TimerWait`、`Event<T>` 与通道（语言语义见[异步与时序模型规范](../specifications/09_AsyncAndTimingModel.md)）。这与原生运行时的分层一致（scheduler.c 微内核 + scheduler.penguin 策略——见 [EmperorPenguin Async](./30_EmperorPenguinAsync.md)）。

## ExternFunctions——C# 内建面

**`ExternFunctions.cs`**（932 行），由 `Build`（15–32 行）注册，全部以 `__builtin.` / `_utils.` 命名空间对齐 Builtin.penguin/Utils.penguin 中的声明：

- I/O：print/println/eprint/eprintln/exit/`__throw_runtime_error`。
- `ICopy<T>.copy`；`AtomicI64` swap/compare_exchange/fetch_add。
- `List<T>` new/at/push/pop/remove/size/set；`Queue<T>` enqueue/dequeue/peek/size；StringBuilder new/append/to_string。
- `_utils` 文件/进程/环境：file_read_text/write_text/size/read_range/append/exe_path/getenv/mkdir/file_exists/dir_exists/dir_get_entries/create_temp_dir；`__builtin._exec_cmd`（sh -c / cmd.exe）；`__args_count/__args_get`。
- `__builtin.string_*`：length/find/find_from/substring/char_at/char_code(_at)/starts_with_at/slice/to_int/to_double；lshift/rshift。
- 调度器/时序：`_sim_*`、`_after`、`_run`（见上）。
