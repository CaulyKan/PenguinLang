# 30. EmperorPenguin Async

EmperorPenguin 用**绑定器脱糖加有栈协程（纤程）**实现 async/wait 语言面（见[异步与时序模型规范](../specifications/09_AsyncAndTimingModel.md)）——不是 LLVM 状态机。本页记录开关门、各脱糖与两层原生运行时。

## 协程门

`enable_coroutine: bool = false`（`EmperorPenguin/src/bound/EmperorPenguinCompiler.penguin` 49–52 行）门控端口、`construct`、`connect`、所有 `wait` 形式、try/catch 与 `async`。无 flag 使用它们时经 `require_coroutine`（`src/bound/SemanticModel.penguin` 423 行）报 `E_UNSUPPORTED "requires the --enable-coroutine option"`——由 `Tests/PortTest/CoroutineSyntaxRequiresFlag.md` 验证。BabyPenguin 无条件实现相同特性。每个 wait/async 还置 `unit.has_suspension = true`（`src/bound/BoundCompilationUnit.penguin` 28–31 行）——无 wait 的程序无论 flag 与否都输出普通顺序 `main`（LLVMEmitter.penguin 779–780 行）。

## wait 脱糖（SemanticBindExpressions.penguin）

`bind_wait_expr`（414 行起）把每种 wait 形式脱糖为两个运行时原语——`_co_wait()`（停靠一个 delta）与 `IFuture.do_wait()`——上的普通控制流：

| 源形式 | 脱糖 |
|---|---|
| `wait;` | `__builtin._co_wait()` |
| `wait <int expr>;` | `_after(n).do_wait()` |
| `wait ev`（`Event<T>`） | `cast<mut IFuture<T>>(new _EventSubscription<T>(ev)).do_wait()` |
| `wait <IFuture>` / `wait <port>` | 转换 + `.do_wait()`（`mk_do_wait_call`，134–143 行） |

bool 操作数与 `change(x)` 操作数是编译错误（`E_TYPE_MISMATCH`）：操作数必须是整数（定时器）、`Event`、`IFuture`、通道或端口。

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

**`std/c/scheduler.c`**（3164 行）——C 微内核：

- 经 POSIX `swapcontext`/`makecontext`（688 行）或 Windows `CreateFiberEx`（701 行）的**有栈协程**。每个协程拥有一个栈；切换即上下文交换，因此 C 调用树任何深处的 `wait` 都停靠整个协程。
- 启动收件箱 FIFO（每线程收养，2464 行）；带 GC 协议握手的 `_co_switch_in`（790 行）恢复；`_co_fingerprint`（1334 行）= 协程冻结栈的哈希（供策略层的静默检测）。
- **MT 执行内核**——mt/free 调度器驱动的运行队列、停靠归档、唤醒路由、定时器表与 future 等待者集合，加上 M4b 工作线程——也在这个文件里；见下方各调度器章节。

**`std/penguin/scheduler.penguin`**（约 1586 行）——用 PenguinLang 写的策略层，与 BabyPenguin 的 SimScheduler 一比一镜像：就绪/下一 delta 队列、`_sched_timers` + `_timer_at`/`_timers_fire`、`_sim_now`/`_sim_delta`/`_sim_activity`/`_sim_settled`、以每协程指纹组合在两个相同的空闲回合上判静默（`__sched_run` 555 行起；头注释："Structurally 1:1 with the old C loop"），以及静默时阻塞在 `poll()` 而非退出的 fd 等待者（`_fd_wait_read`/`_fd_wait_write`）——stdio 语言服务器依赖的机制。

该分层镜像 BabyPenguin：微内核拥有栈与切换（必须由 C 完成）；调度策略（delta、定时器、静默）是 Penguin 层库代码，靠跨编译器 async/timing 测试类（`Tests/AsyncTest/`、`Tests/TimingTest/`、`Tests/PortTest/`、`Tests/ChannelTest/`——全部在 BabyPenguin 与 Pass3 上以逐字节一致的期望运行）保持两个运行时一致且可测。

## 时钟模型（`--enable-multithread-scheduler --enable-clock-model`）

`main.penguin` 交换自动装载的策略：`std/penguin/scheduler_mt.penguin` 替换 `scheduler.penguin`（公共面不变、命名空间相同，发射器零改动）。收敛式 delta 语义——每 delta 完全收敛、边界七步、所有协程结束才退出——定义在设计文档 `docs/zh/wip/concurrent_scheduler.md`。`wait <n>` 降低到 `__builtin._mt_wait_ticks(n)`（`SemanticBindExpressions.bind_timer_wait`）：`n < 0` 纯让出（`_co_yield`，仍在当前 delta 内可运行）；`n == 0` 经 `_co_wait()` 停靠，与裸 `wait;` 完全一致（定时器路径表达不了它——`_after(0)` 注册的截止点等于当前时刻，`do_wait` 的先 poll 快路径会在同一 delta 内当作"就绪"直接返回、变成空操作；由 `Tests/MtSchedulerTest/MtWaitZeroEqualsBare.md` 钉住）；`n > 0` 走定时器条件等待。

Pass 10（封闭性分析，见[绑定模型](./26_EmperorPenguinBound.md)）在该标志下运行：它标记可窃取的 `async` spawn 点，供工作线程分发；`-Wconcurrency` 按 spawn 点打印可窃取/钉住诊断及钉住原因链。

### 工作线程（`EMPEROR_THREADS=N`）

`EMPEROR_THREADS=N`（环境变量，默认 1）启动 N−1 个工作线程；一个 delta 在全部 N 个线程上并行——每个线程抽干自己的队列，Pass 10 判为可窃取的未运行协程在 delta 开始时轮转分发、空闲线程可窃取。delta 在全局栅栏处结束：所有线程同时无活可干。决策 A（已启动的协程永不迁移）让唤醒路由无竞争——唤醒发给协程的所有者，而所有者按定义不在执行它。`EMPEROR_THREADS` 未设置时单工作者即调用线程本身，与单工作者内核逐字节一致。

### 每线程时间分析（`EP_MULTITHREAD_SCHEDULER_PERF_ANALYZE=1`）

置 1 后把每个调度线程的墙钟时间切成四个相位，程序退出时（工作者线程 join 之后）向 stderr 打印每线程报告：

- **busy** —— 在 `_emperor_co_switch_in` 内：执行 penguin 协程代码。
- **idle** —— 停靠等活（`mt_idle_wait` 睡眠 + delta 间停靠；futex 前的自旋窗口也计入 idle）。
- **schedule** —— 调度簿记：运行队列、唤醒路由、窃取、边界处理。
- **gc** —— 在 GC 驱动内（gc.c 在收集入口处调用调度器的相位钩子）。

时间为每线程微秒值加上占该线程自身已测总量的百分比，末尾一行 `total:` 汇总；classic 模式的收集（没有 mt 槽位）与调度器启动前的时间不计入。未设置时代价为每次相位切换一次宽松读加比较。由 `Tests/FreeMtTest/FreeMtPerfAnalyzeReport.md` 钉住。

### 停顿诊断（设计文档第 12 章："不要无声挂起"）

两类停顿由 `std/c/scheduler.c` 中的预算皮带来诊断，而不是永远无声挂起：

| 类别 | 检测方式 | 环境旋钮（默认） | 退出码 |
|---|---|---|---|
| (a) 单个 delta 永不收敛——互相唤醒的活锁或 `while (!cond) wait -1;` 自旋 | 当前 delta 内的恢复次数超过步数预算（`mt_run_one` 计数，`_emperor_mt_run_delta` 每 delta 重置） | `EMPEROR_SCHED_STEP_BUDGET`（20,000,000；0 关闭） | 60 |
| (b) 时钟被钉住——delta 持续收敛但时间等待者一直 pending、没有任何定时器触发；典型是 `while (!cond) wait;`（`wait 0;` 自旋同理） | 连续收敛的 delta 数带着 pending 未触发的定时器（`_emperor_mt_fire_due` 只在定时器以**严格更晚**的截止点触发时重置计数——真正的时钟进展，`wait 0` 重复注册同一截止点不算） | `EMPEROR_SCHED_PIN_BUDGET`（20,000,000；0 关闭） | 61 |

两者都向 stderr 打一条诊断（`emperor sched: stall (exit N): …`，含疑似源码模式与存活协程数），并经由与 `exit()` 相同的通道停止程序。它们是皮带不是检测器：合法的长 delta（立即可见容器的深流水线）或长时间同 tick 搅动（大延迟流水线）与默认值差着数量级——`Tests/MtSchedulerTest/MtStallBudgetsQuiet.md` 用总量超过预算的紧皮带运行以钉住重置逻辑。不含让出点的纯 `while (true) {}` 不会被捕获（没有任何恢复返回调度器）——与今天一致。测试：`MtStallLivelockDiag`（60）、`MtStallClockPinnedDiag`（61）。

决策 E 挂起（只有所有协程都结束才退出；一个停靠且无唤醒源的协程会永远挡住它）有专属报告器：一个收敛的 delta 什么都没运行、没有欠账停靠者、没有定时器也没有 fd 等待者，而协程仍然存活，即意味着唤醒丢失。`EMPEROR_IDLE_DUMP=N`（默认 1000 回合，0 关闭）把每个存活协程的停靠归档倾倒到 stderr 并以退出码 63 停止，而不是无声挂起。

### 分配硬上限压力阀（设计文档 10.1：M5 v0）

一个 delta 可以任意长，而回收只在边界停世界进行——mt 内核下（poll 在并行窗口中推迟到边界）紧循环分配的协程会把堆无限撑大。分配器带一个硬上限（`std/c/gc.c`）：下一次分配的 charge 会越限时，触碰者停靠在"等回收"上（`_emperor_sched_alloc_park`，CO_COND 状态、**不计入可运行**），全体分配阻塞时 delta 自然收敛到边界、停世界收集、释放停靠者、分配重试——"全阻塞"的含义是"现在就回收"，既不死锁也不 OOM。

- 旋钮：`EMPEROR_GC_HARD_CAP`（字节；缺省 8 GiB——远高于任何真实负载（自举峰值约 0.6 GiB）而失控分配风暴会停靠而不是吃光机器；显式 `0` = 不限）。
- **确定性 OOM（退出码 62），不是皮带**：边界收集后若"活集 + 最小待决 charge"仍放不下（`_emperor_gc_cap_blocks`，停靠表跟踪最小 charge），任何收集都永远无法解除阻塞——经 `exit()` 通道报告并停止。判定必须含 charge：`活集 > 上限` 单独不够，停靠者会卡在"活集"与"活集+charge"的缝隙里（实测一个保活循环恰好停在 cap−48 处永久停靠、每次收集零释放）。单次分配本身就大于上限同理立即 62。
- 作用域：阀门只在 mt 内核下停靠；经典调度器 / 主栈策略代码 / 顺序回退平台上分配照常进行（上限在那里只是名义值，增长仍由既有内联应急收集与目标节奏约束——经典路径行为与改动前逐字节一致，`MtGcHardCapClassicAdvisory` 钉住）。分配检查在分配器锁之外（并发的越限余量 = 每线程一个在飞 charge，阀门语义可接受）。
- 顺带修复：8×目标应急收集在并行窗口内不再内联执行（内联标记会与 worker 变更器竞争）——推迟到边界，窗口内的增长由硬上限约束。
- 轮询侧 STW 升级（`EMPEROR_GC_STW_MS`，默认 250 毫秒，0 = 关闭）：边界排不掉的 want_collect——delta 永不收敛（忙服务在永恒 delta 里持续搅动）——会升级：轮询协程把自己挂到硬上限阀的停靠列表上，逼出边界收集所需的收敛。没有它，永恒 delta 会把收集一直压到堆爬到硬上限——健全，但堆顶在上限而不是目标值。计时钟从第一次观察到标志的轮询开始计时，标志落下即重新武装。
- 测试：`MtGcHardCapParkCollect`（4 路并行 churn 穿过 256KB 上限正常完成、校验和不変）、`MtGcHardCapOom`（保活 62）、`MtGcHardCapSingleAllocOom`（单次超限 62）、`MtGcHardCapZeroUnlimited`（显式 0 = 不限）。

## 自由形态运行时（`--enable-multithread-scheduler`）

`main.penguin` 再次交换策略：`std/penguin/scheduler_free.penguin`——与 `scheduler_mt.penguin` 同一个 C 微内核、同一公共面，面向不使用仿真时间的服务：

- **没有虚拟时钟**：没有可推进的 tick、没有定时器表；`wait n;`（n > 0）在运行期被拒绝（没有可等的 tick——改等 Event、future 或 AsyncQueue）；`wait;` / `wait 0;` 停靠一个收敛回合，负数 `wait` 是纯让出，与 sim-mt 完全一致。
- **组件立即化**：Signal/Fifo/LatestChannel/Event 内联提交并唤醒——写操作的效果在被唤醒的读取者的**同一**收敛回合内可见（sim-mt 的延迟提交层被反转；边界上没有东西可提交）。
- **跨线程结构**走 `AsyncQueue<T>`：由 AtomicBool 自旋锁保护的 MPMC 队列（test-and-set 失败就以纯让出退避——绝不跨停靠持锁）；取空者作为条件等待者停靠、由入队唤醒。它实现 `ISync`，因此持有它的协程是 Pass 10 可窃取的，在 `EMPEROR_THREADS` 下真正跑在不同线程；立即的 Signal/Fifo/Latest/Event 保持单域（Pass 10 钉住其持有者，与 sim-mt 同一格）。
- 边界只保留：欠账/可运行检查 → 非阻塞 fd 探测 → 只剩 fd 等待者时阻塞 poll → 所有协程结束才退出，否则按设计挂起（决策 E；`EMPEROR_IDLE_DUMP` 负责报告）。

对不使用仿真时间的程序，free-mt 与 sim-mt 行为一致——free-mt 的存在让服务不必为虚拟时钟付费，也让 `wait n` 大声失败而不是默默排在一个永不推进的 tick 上。测试：`Tests/FreeMtTest/`（仅 Pass3）。

## 与 GC 的交互

协程栈经帧链机制被 green tea 收集器枚举（见 [EmperorPenguin GC](./29_EmperorPenguinGC.md)）：每个被停靠协程的帧链是精确根集，保守栈覆盖只作用于活动栈。`_co_switch_in` 与 GC 协调，使回收绝不观察到切换到一半的栈。
