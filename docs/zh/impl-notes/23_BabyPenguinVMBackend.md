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

**`SimScheduler.cs`**（612 行，单例）——M0 调度器语义的 C# 参考实现：完全收敛的 delta tick、七步边界、全部协程结束才退出的退出规则、钉住 tick 的裸等待与纯让出的负数等待。原生 mt 内核（`std/c/scheduler.c` + `scheduler_mt.penguin`，见 [EmperorPenguin Async](./30_EmperorPenguinAsync.md)）实现同一套语义；跨编译器测试类别（`Tests/MtSchedulerTest/`、`Tests/TimingTest/`）同时钉住两者。

- **Delta**（`Run`，187 行）：一切可运行的都运行到无可运行——中途收养的 spawn 在其 spawn 所在的 delta 内运行（`AdoptSpawns` 361 行），纯让出重新排到同一 delta 的队尾，被踢醒的条件停靠者在踢醒它的那个 delta 恢复。每次恢复计为当前 delta 的一步（`RunOneDeltaJob` 394 行）：状态 3/4 = 完成；状态 2 = 生成器产出了一个值（延迟踢——消费者在下一个 delta 重新轮询）；状态 0 = 停靠，按旁路停靠注记归档。
- **停靠归档**（`SchedParkNote`）：VM 的停靠指令就是一个普通的 Blocked 返回，因此紧挨在 `wait` 之前的 Penguin 层辅助调用会在调度器上设置注记——`PureYield`（`wait -1`：重新入队，仍可运行）、`FuturePark`（do_wait/通道背压：不欠任何东西，只能被踢醒）、无注记（裸 `wait;`/`wait 0;`：欠当前 tick 上的下一个 delta）。
- **边界**（七步，`Run` 254–325 行）：延迟踢（通道族立即提交，只有唤醒是延迟的——本 delta 流动的事务在下一个 delta 唤醒其等待者，`KickFutures` 172 行）→ 触发到期定时器（`FireDueTimers` 499 行；以严格更晚的截止触发会重置钉住连击）→ 有欠账的裸等待停靠者或任何新的可运行者 → 在**同一** tick 进入下一个 delta（时钟不许跳过欠账的停靠者）→ C# VM 没有 fd 源 → 把 tick 推进到最早的未来截止 → 仅当每个协程都结束时退出（`_liveJobs` 为空）；停靠且无唤醒源**不是**退出（决策 E——循环改为安静休眠）。
- **停滞保险**（与原生内核一致，`ParseBudgets` 327 行）：`EMPEROR_SCHED_STEP_BUDGET`（默认 20,000,000，0 关闭；退出码 60——单个 delta 内的恢复次数超限，零时间循环或活锁）与 `EMPEROR_SCHED_PIN_BUDGET`（默认 20,000,000，0 关闭；退出码 61——delta 持续收敛但时间等待者一直挂起且没有任何定时器以严格更晚的截止触发，典型是 `while (!cond) wait;`）。两者都打印一条诊断并终止程序（`StallAbort` 343 行）。
- 定时器：`__builtin._after(n)`（ExternFunctions.cs 780 行）分配 `_TimerWait` future 并经 `EnqueueTimerFuture(deadlineTick, future)`（156 行）注册到当前作业上。
- 时序 extern（ExternFunctions.cs）：`_sim_now`（756）、`_sim_delta`（763——单调递增的 delta 回合计数器，接线同回合合并写入）、`_sim_settled`（772——上一个完成的回合没有事务活动且本回合至今也没有时为真；稳定点端口读取停靠到该条件成立）、`_sim_activity`（82）、`_after`（780）、`_run`（854——驱动 `SimScheduler.Run`，把 `Exited` 转为 `ProgramExitException`）。
- 停靠/踢醒 extern：`_sched_yield`（829）、`_sched_future_park`（834）、`_sched_defer_kick`（841——标记一个让等待者在**下一个** delta 才能观察到的事务）、`_sched_kick_futures`（848——立即踢醒全部条件停靠者）。

协程运行时本体是 `BabyPenguin/Builtin.penguin` 中的 **Penguin 层代码**：`Scheduler`（739 行，`pending_jobs : mut _utils.Queue<IFutureBase>`）、`RoutineContext`（777 行；其 `.call()` extern 重入被挂起的 `RuntimeFrame`——C# 侧 `RoutineContextCall`，ExternFunctions.cs 308–374 行）、`IFuture`/`IFutureBase`（659–669）、`_TimerWait`（960）、`Event<T>`（863）与通道族（`Fifo` 1095、`LatestChannel` 1240、`MergeChannel` 1414——每条提交路径都经 `_sched_defer_kick`；语言语义见[异步与时序模型规范](../specifications/09_AsyncAndTimingModel.md)）。这与原生运行时的分层一致（scheduler.c 微内核 + scheduler.penguin 策略——见 [EmperorPenguin Async](./30_EmperorPenguinAsync.md)）。

## ExternFunctions——C# 内建面

**`ExternFunctions.cs`**（961 行），由 `Build`（15–32 行）注册，全部以 `__builtin.` / `_utils.` 命名空间对齐 Builtin.penguin/Utils.penguin 中的声明：

- I/O：print/println/eprint/eprintln/exit/`__throw_runtime_error`。
- `ICopy<T>.copy`；`AtomicI64` swap/compare_exchange/fetch_add。
- `List<T>` new/at/push/pop/remove/size/set；`Queue<T>` enqueue/dequeue/peek/size；StringBuilder new/append/to_string。
- `_utils` 文件/进程/环境：file_read_text/write_text/size/read_range/append/exe_path/getenv/mkdir/file_exists/dir_exists/dir_get_entries/create_temp_dir；`__builtin._exec_cmd`（sh -c / cmd.exe）；`__args_count/__args_get`。
- `__builtin.string_*`：length/find/find_from/substring/char_at/char_code(_at)/starts_with_at/slice/to_int/to_double；lshift/rshift。
- 调度器/时序：`_sim_*`、`_after`、`_sched_yield`/`_sched_future_park`/`_sched_defer_kick`/`_sched_kick_futures`、`_run`（见上）。
