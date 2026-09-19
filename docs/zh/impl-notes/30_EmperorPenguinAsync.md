# 30. EmperorPenguin Async

EmperorPenguin 用**绑定器脱糖加有栈协程（纤程）**实现 async/wait 语言面（见[异步与时序模型规范](../specifications/09_AsyncAndTimingModel.md)）——不是 LLVM 状态机。本页记录开关门、各脱糖与两层原生运行时。

## 协程门

`enable_coroutine: bool = false`（`EmperorPenguin/src/bound/EmperorPenguinCompiler.penguin` 49–52 行）门控端口、`construct`、`connect`、所有 `wait` 形式、try/catch 与 `async`。无 flag 使用它们时经 `require_coroutine`（`src/bound/SemanticModel.penguin` 423 行）报 `E_UNSUPPORTED "requires the --enable-coroutine option"`——由 `Tests/PortTest/CoroutineSyntaxRequiresFlag.md` 验证。BabyPenguin 无条件实现相同特性。每个 wait/async 还置 `unit.has_suspension = true`（`src/bound/BoundCompilationUnit.penguin` 28–31 行）——无 wait 的程序无论 flag 与否都输出普通顺序 `main`（LLVMEmitter.penguin 779–780 行）。

## wait 脱糖（SemanticBindExpressions.penguin）

`bind_wait_expr`（414 行起）把每种 wait 形式脱糖为两个运行时原语——`_co_wait()`（停靠一个 delta）与 `IFuture.do_wait()`——上的普通控制流：

| 源形式 | 脱糖 |
|---|---|
| `wait;` | `__builtin._co_wait()` |
| `wait n tick;` / `wait <int expr>;` | `_after(n).do_wait()` |
| `wait change(x)` | `{ let __wait_sample = x; while (x == __wait_sample) { _co_wait(); } x }` |
| `wait <bool condition>;` | `while (!cond) { _co_wait(); }` |
| `wait ev`（`Event<T>`） | `cast<mut IFuture<T>>(new _EventSubscription<T>(ev)).do_wait()` |
| `wait <IFuture>` / `wait <port>` | 转换 + `.do_wait()`（`mk_do_wait_call`，134–143 行） |

端口裸读沉淀点与读权限矩阵在同一遍处理（端口通道字段经同一 future 机制读取）。

## async 脱糖（bind_spawn_async，约 2300–2700 行）

每个 `async f(args)` 调用点为该点合成两个辅助类：

- 实现了 `__builtin.__ICoroutineEntry.__enter` 的 `__SpawnCtx_<n>`——捕获接收者与实参、调用函数、用结果完成 future（`fut.complete(result)`）。
- `__SpawnFut_<n>`——字段 `done`/`result`、`complete()` 方法与返回 ready_finished/not_ready 的 `IFuture<R>.poll()` 实现（void 用 `__SpawnUnitFuture`）。

调用点变成 `{ let __s = new __SpawnCtx_...(args); __builtin._co_spawn_entry(__s); __s.fut }`。错误：E_ASYNC_INVALID "'async' requires a function call" / "cannot spawn a static member call"。

生成器降级到同一协程机制：合成 `__GenCtx` 上下文对象；`yield` 发布值并停靠到下一次 `next()`（`Tests/AsyncTest/YieldIterateTest.md`）。

## 发射

`LLVMEmitter.penguin`：`needs_sched_run`（311 行）；挂起模式下 `emit_main` 用 `call void @_emperor_co_spawn_fn0(ptr @initial)` 启动每个 initial（1744–1755 行），再调用 `@__builtin___sched_run()`（1766–1775 行）。`std/penguin/scheduler.penguin` 随 `--enable-coroutine` 自动加载（1150 行）。

## 原生运行时——两层

**`std/c/scheduler.c`**（约 930 行）——C 微内核：

- 经 POSIX `swapcontext`/`makecontext`（234–252、329–363 行）或 Windows `CreateFiberEx`（309、376 行）的**有栈协程**。每个协程拥有一个栈；切换即上下文交换，因此 C 调用树任何深处的 `wait` 都停靠整个协程。
- 启动收件箱 FIFO（202–232 行）；带 GC 协议握手的 `_co_switch_in` 恢复；`_co_fingerprint` = 协程冻结栈的哈希（供策略层的静默检测）。

**`std/penguin/scheduler.penguin`**（约 1578 行）——用 PenguinLang 写的策略层，与 BabyPenguin 的 SimScheduler 一比一镜像：就绪/下一 delta 队列、`_sched_timers` + `_timer_at`/`_timers_fire`、`_sim_now`/`_sim_delta`/`_sim_activity`/`_sim_settled`、以每协程指纹组合在两个相同的空闲回合上判静默（`__sched_run` 555 行起；头注释："Structurally 1:1 with the old C loop"），以及静默时阻塞在 `poll()` 而非退出的 fd 等待者（`_fd_wait_read`/`_fd_wait_write`）——stdio 语言服务器依赖的机制。

该分层镜像 BabyPenguin：微内核拥有栈与切换（必须由 C 完成）；调度策略（delta、定时器、静默）是 Penguin 层库代码，靠跨编译器 async/timing 测试类（`Tests/AsyncTest/`、`Tests/TimingTest/`、`Tests/PortTest/`、`Tests/ChannelTest/`——全部在 BabyPenguin 与 Pass3 上以逐字节一致的期望运行）保持两个运行时一致且可测。

## 与 GC 的交互

协程栈经帧链机制被 green tea 收集器枚举（见 [EmperorPenguin GC](./29_EmperorPenguinGC.md)）：每个被停靠协程的帧链是精确根集，保守栈覆盖只作用于活动栈。`_co_switch_in` 与 GC 协调，使回收绝不观察到切换到一半的栈。
