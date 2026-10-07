# 多线程调度器实施计划（第一阶段：单线程新调度器 + 装载路由）

日期: 2026-09-29
设计文档: `docs/zh/wip/concurrent_scheduler.md`（唯一事实源）
分支: `feature/multithread-scheduler`

## 目标（本阶段）

实现设计文档的**单线程新调度器**（收敛式 delta + 边界七步 + 新退出规则 + 条件等待），
通过 `--enable-multithread-scheduler` 启用；**旧调度器（scheduler.penguin）完全不动**，
默认行为零变化。多线程（工作窃取、封闭分析 Pass 10、GC）是后续阶段。

## 关键事实（已核实）

- 装载链：`main.penguin:161` 按 `config.coroutine_enabled` 读入 `scheduler.penguin`；
  `LLVMEmitter:1843` 按名字调用 `@__builtin___sched_run()`。**换文件即换调度器，发射器零改动**。
- `_co_switch_in` 返回 status：0=结束, 1=delta 停靠, 2=fd 停靠, 3=exit。
- 停靠实现三件套（`scheduler.c:536-563`）：置 state、记 `sp_park`、`_emperor_gc_scan_set_live`。
- 定时器表、活动计数、指纹全在 Penguin 策略层。

## 改动清单

### 1. C 微内核（`EmperorPenguin/std/c/scheduler.c`，加法式）

- 新 state：`CO_YIELD = 6`（让出，仍在当前 delta）、`CO_COND = 7`（条件停靠，不欠 delta）。
- 新 extern：`_emperor_co_yield()`、`_emperor_co_park()` —— 与 `_emperor_co_wait` 相同的
  停靠三件套，只是 state 不同；顺序回退平台为 no-op/继续。
- `_emperor_co_switch_in` 返回映射增加：state 6 → 返回 6；state 7 → 返回 7。
  旧调度器不可达这两个 state（没有调用者），零影响。

### 2. 新调度器策略（`EmperorPenguin/std/penguin/scheduler_mt.penguin`）

以 `scheduler.penguin` 为底本，修改：

- extern 增补 `_co_park()` / `_co_yield()`。
- `__SchedCo` 增 `parked: mut bool`（唤醒幂等）。
- 全局：`_sched_ready`（当前 delta 队列）、`_sched_delta_owed`（下一 delta 队列，bare wait 用）、
  `_sched_future_waiters`（通用 future 等待者，kick 集合）、`_sched_running`（当前在跑的协程，
  库代码经 `__sched_current()` 取"我"）、定时器表改为（deadline, co）。
- 辅助：`__sched_current()`、`__sched_park_me()`（标 parked + `_co_park`）、
  `__sched_wake(co)`（parked 时入 ready）、`__sched_kick_futures()`（唤醒全部 future 等待者）、
  `__sched_future_wait()`（登记 + 停靠，醒来后自移除）。
- `IFuture.do_wait` 重写：先 poll（就绪即同 delta 交付，不再"先停一轮"）；未就绪走
  `__sched_future_wait()` 循环。唤醒源由投递方 kick。
- `_after(n)`：登记（deadline, 当前协程）到定时器表。
- 投递方 kick：`Fifo.deliver`、`LatestChannel.write/try_write`（push 内）、`_Fanout.write_all`、
  `Event.emit`；`Fifo.take`（腾容量）与 `close` 额外唤醒 Fifo 的写者等待表。
- `Fifo.write` 背压：`while (is_full()) { writers.push(current); park; check_closed; }`，
  take/close 唤醒并清空 writers（虚假唤醒由循环重查吸收）。
- `Event.emit` 去掉尾部裸 `wait`（投递即 kick，同 delta 唤醒等待者）。
- `__sched_run` 重写为边界七步：
  1) 触发到期定时器（deadline ≤ now）→ 唤醒；
  2) 有活（定时器唤醒 / delta-owed 的 bare wait）→ 同 tick 下一个 delta；
  3) 非阻塞 `_fd_poll(0)` → 有唤醒 → 下一个 delta；
  4) 有未来定时器 → 推进 tick → 回 1；
  5) 只剩 fd 等待者 → 阻塞 `_fd_poll(-1)` → 下一个 delta；
  6) 全部结束 → 退出；
  7) 否则挂起（`_fd_poll(10)` 睡眠循环，不烧 CPU，决策 E）。
  子协程结束（status 0）时 kick futures（父等 async 结果的唤醒源）。
- 指纹与活动计数退役：函数保留为 no-op（兼容引用），不再参与任何判定。

### 3. 编译器（最小改动）

- `CompilerConfig.penguin`：新字段 `multithread_scheduler_enabled: bool = false`；
  解析 `--enable-multithread-scheduler` / `-enable-multithread-scheduler`；开启时强制
  `coroutine_enabled = true`。
- `main.penguin`：`--enable-multithread-scheduler` 时读入 `scheduler_mt.penguin`
  （替代 scheduler.penguin，SourceInput 名仍为 "scheduler.penguin" 以保持 libmeta/命名一致——
  文件内 namespace 相同）。帮助文本补一行。
- 语义层、发射器：零改动（所有名字不变）。

### 4. 测试（`Tests/MtSchedulerTest/`，EP-only：Apply To = EmperorPenguin Pass2, Pass3）

- WaitPinsTick：0.3 节反例的正向版（条件等待背压 + `wait 10` 消费者，验证完成与时序）。
- SameDeltaDelivery：通道写后同 delta 读到（就绪 fast-path）。
- BackpressureConditionWait：容量 1 多写，验证不再依赖轮询。
- TimerAdvance：`wait n` 与 delta 收敛交错。
- ExitAllDone：全部协程结束即退出（无指纹）。
- 旧调度器回归：全矩阵跑一遍确认零变化。

## 进度（2026-09-29）

- [x] 单线程新调度器（scheduler_mt.penguin 收敛 delta + 七步边界 + 新退出规则 + 条件等待）
- [x] `--enable-multithread-scheduler` 装载路由（fc56c027）
- [x] `wait -1` 纯让出 + M1 原子量（054f59eb）
- [x] M3 微内核逐线程化：TLS 执行态、每线程收件箱、线程注册表（e30699c8）
- [x] M4a 执行引擎下沉 C：`_emperor_mt_run_delta`（队列/唤醒路由 CAS/定时器表/future 等待集），
      scheduler_mt.penguin 瘦身为薄边界（`__sched_run` 只跑七步）。M4b 加 worker 线程时该循环零改动。
- [x] M2（lite）：Pass 10 封闭分析（SemanticConcurrency.penguin）—— Shareable 格（Imm=基元+string、
      Sync=ISync 标记（原子类已 impl）、ValueShare=值类字段递归；枚举/接口/闭包 v1 保守不可共享）、
      effects 静态闭包（全局/extern/虚调用/meta/不可解析即 pin）、spawn 点判定 + `_co_spawn_entry_stealable`
      重写（IR 层换名）+ `-Wconcurrency` 诊断。虚调用并集、初始例程判定、libmeta 边界留待后续
- [x] M6 第一块：`Signal<T>` 延迟提交构件（scheduler_mt.penguin）—— write 挂起/读已提交值/
      边界按注册序确定性提交并唤醒等待者；`wait sig` 为 first-observer 语义（时钟进程形态
      `while { wait sig }` 每沿恰好一值）。单写者契约；非 ISync（封闭在单域，Pass 10 视为
      不可共享 ⇒ 持有者 pin）。实现备注：泛型裸字段与带默认值字段混用会触发发射器缺陷
      （泛型零值文本为空 → 非法 IR），字段全部改为构造器赋值绕开；构造器参数不可命名
      `initial`（关键字，同 pure 坑）。
- [x] M4b：已交付（c182ef82）—— EMPEROR_THREADS=N、worker 线程 + 可重用屏障、spawn 时刻分发、
      偷取 CO_READY+stealable、定向唤醒、分配器锁 + 边界 STW 收集、print 行锁（细节见提交信息）
- [x] M6 第二块：延迟 `Fifo`（mt 语义下默认）—— 写入 pending 队列，边界按写序冲刷进 committed
      并唤醒读者（每跳恰好一个 delta）；容量判定含 pending（背压停靠即仲裁，域内写序确定 ⇒ 无需
      身份 tie-break）；`Queue`/`List` 保持立即。MtSameDeltaDelivery 期望从 @1 改 @2（迁移点），
      新增 MtDeferredFifoPipeline（三段流水线两跳两 delta、tick 0，N=4 字节一致）。
      BabyPenguin 不受影响（走 scheduler.penguin）。
- [x] 关键字作标识符系统性扫除（Parser）：保留字集合（pure/!pure/extern/export/async/!async/
      initial/wait/tick）在参数名、局部 let 名、类字段名三处报定位 E_PARSE（此前只堵了函数名一处；
      Signal<T> 的构造器参数 `initial` 就是同一坑——上轮误诊为"泛型零值缺陷"，实为同一根因）。
      类型关键字（double 等）按事实语言保持合法。
- [ ] 已知发射器缺陷：字符串字面量含非 ASCII（如 em dash）时，长度字段按字符计而数组按 UTF-8
      字节计 → 非法 IR（bootstrap13 撞上，消息已改 ASCII 绕开；待修 LLVMEmitter 的字面量长度）
- [x] M6 第三块：`LatestChannel`（线/端口底座）延迟化—— take 只服已提交事务+种子；
      push 仅置 pending（同 delta 坍缩即单写者合并）并注册边界 commit；write 不再立即 kick
      （Event.emit 同理改挂 `_sched_deferred_kick`，边界统一 kick）。current() 保持立即
      （诊断读，非事务）。MtDeferredWire 钉语义（坍缩值下一 delta 交付，N=4 字节一致）。
      至此延迟构件族齐：Signal / Event / Fifo / LatestChannel（线）；Queue/List/裸字段立即。
- [x] 非 ASCII 字面量缺陷修复：LLVMEmitter 增加 utf8_byte_len（码点加权 + 宿主探针——
      C# VM 的 string_length 按 UTF-16 单元计、原生按字节计，探针字面量"中"区分两宿主）；
      字面量数组尺寸与长度字段改用字节长度。中文字符串+em-dash 实测通过。
- [x] M0 半部：删 `wait n tick` 长写法（g4 产生式 + EP 解析的 Tick 消费；`wait n` 短形式两编译器
      语义本就一致）；迁移 51 处用例站点（Tests/*.md 48 + ESL 库 2 + Examples/Uart 1 + LSP fixture 1）
      ——注意教训：语法迁移必须扫**全仓 .penguin**，不只 Tests（Clock.penguin 漏扫导致 3 例空操作数 IR）
- [x] M6 收尾件：`--shuffle-deltas` 顺序扰动检测器（EMPEROR_SHUFFLE_DELTAS=<seed>，scheduler.c：
      每 delta 开始对就绪队列做种子化 xorshift Fisher-Yates，仅单 worker；多线程下并行调度本身
      就在置换顺序）。确定性程序（延迟构件）多种子字节一致；顺序依赖程序（裸 println initials）
      各种子输出不同。注意：无等待点的程序不进调度器（发射器判非挂起模块、初例直接顺序调用）——
      检测器只对含挂起点且多协程同 delta 的程序有意义。MtShuffleDeltasStable 钉住确定性侧。
- [x] 基线性能评估（2026-10-01，i9 级桌面、pass1 编译、每项 5 轮中位/3 轮）：
      | 基准 | 经典 | mt N=1 | mt N=4 |
      |---|---|---|---|
      | b1 spawn/join ×2000 | ~129ms | ~119ms | ~129ms |
      | b2 定时器 ×20000 | ~53ms | ~25ms（2.1×）| — |
      | b3 Fifo 20k 条（经典 20001 轮 vs mt 314 delta 批量搬运）| ~47ms | ~14ms（3.4×）| — |
      | b3×10 RSS | 67.7MB | 31.5MB（少 53%）| — |
      | b4 计算 fan-out 4×2000 万 | ~217ms | ~215ms | ~79ms（2.7×，单跑最优 61ms）|
      | RSS（b4）| 4.5MB | 6.6MB | 13MB（每 worker 约 +2MB 栈保留）|
      结论：N=1 无回归（b1/b4 持平）；b2/b3 反而更快——收敛式 delta 让每 delta 搬一批
      （b3 轮数 20001→314），减少切换与轮转开销；GC 侧 b3×10 峰值 RSS 减半（批次化分配
      局部性 + 更少收集触发）。b1 N=4 无收益（串行依赖链，spawn 本就 pin 在主线程——符合预期）。
      注意：b3 的经典/mt 输出值相同但 delta 语义不同（立即 vs 延迟），吞吐差部分来自语义
      （每 delta 批量提交）而非纯调度开销。
- [x] M6 交付件：`Ownership<T>` v1（core_builtin 类 + 绑定器构造检查 + Pass 10 Own 格入口）——
      构造新鲜性 v1 接受：原始字面量（constant/string/bool，值无别名）、`new` 表达式、`.move()`
      结果（独占传递）；线性持有局部需要逃逸分析，留作精度提升（v1 限制性但健全）。
      `.move()` 运行期消耗检查（双 move → 运行期错误 code 101）；字段 `__` 前缀约定（binder 拒绝
      `.owned` 直访留后续）。Pass 10：Ownership<U> 对任意 U Shareable ⇒ 携带可变引用的 spawn
      也能 STEALABLE（跨线程移动引用的唯一受制裁通道，N=4 实测）。注意坑：NewExpression 的
      type_name 带命名空间前缀（`__builtin.Ownership`），检查用 string_find 而非前缀匹配。
- [x] Ownership 健全性收口：绑定器拒绝 Ownership 接收者上的 `.__payload`/`.__state` 成员访问
      （直读泄漏负载绕过消耗 = 独占性作废；直写可伪造状态）——错误信息指向 `.move()`/`.is_owned()`。
      MtOwnershipPayloadPrivate 负向用例。线性持有局部的接受仍是 v1 限制（需逃逸分析，
      注释中已如实记录而非静默近似）。
- [x] M0 另半部：删 `wait cond`/`wait change(x)`——两编译器 binder 一律 E_TYPE_MISMATCH
  （"wait on an Event, a channel, a future or a port transaction instead"）；ICodeContainer
  的 IsChangeUnit/bool-wait 编译分支与 EP 的 wait_change 降低整体删除（含 wait_change_operand
  辅助；wait_is_change_unit 保留供报错分支探测）。5 个用例迁移到 Event/端口事务形态
  （WaitCondition×2/WaitChangeVariable/WaitChangePort/PortConditionWait；PortBareReadSettlePoint
  仅描述修订），2 个红哨兵（WaitConditionRemoved/WaitChangeRemoved）锁死删除本身。
  BabyPenguin 调度器语义对齐（C# 重写 SimScheduler）仍是独立专项，未含在此。
- [x] M0 文档收尾（7b5a7e84）：两棵文档树清除三种已删形式（09/10 规范、两个教程、
  impl-notes 20/25/30，双语 1:1）；等电平/边沿检测改写为事务循环惯用法（对齐
  PortConditionWait/WaitChangePort）；顺带修正 10 规范过期种子断言——output 默认值是电平，
  `wait port` 不交付 time-0 种子（seedprobe 实测 v=false t=5）；zh/wip 第 2 章现状断言
  更新（裸 wait 由回合活动门在当前 tick 服务）。make docs-site 四项结构校验全绿。
- [x] M0 两类停顿诊断（设计文档 12 章、M0 验收项"两类停顿诊断可用"）：scheduler.c 皮带预算——
  (a) 单 delta 收敛步数预算（EMPEROR_SCHED_STEP_BUDGET，`mt_run_one` 计数、每 delta 重置，
  超限 stderr 诊断 + 退出码 60，捕获 `wait -1` 自旋/唤醒活锁）；(b) 时钟钉住预算
  （EMPEROR_SCHED_PIN_BUDGET，连续收敛 delta 有 pending 定时器却无触发，退出码 61，捕获
  `while (!cond) wait;` 重试循环）。默认各 20M、0 关闭；(b) 的重置只认**严格更晚截止点**的
  触发（真时钟进展），故 `wait 0` 重复注册同一截止点的自旋也会被抓。MtStallLivelockDiag(60)/
  MtStallClockPinnedDiag(61)/MtStallBudgetsQuiet（紧皮带+总量超限验证两处重置逻辑）。
- [x] 顺带修复 mt 下 `wait 0` 语义分歧（违反 M0 "`wait;`=`wait 0;`" 统一）：`_mt_wait_ticks`
  的 n==0 原走定时器路径——`_after(0)` 截止点==now，do_wait 先 poll 快路径当轮就绪 → 同 delta
  空操作（a1 a2 b1），与 bare wait/经典调度器/BabyPenguin（都是 a1 b1 a2）背离。n==0 现改走
  `_co_wait()`。MtWaitZeroEqualsBare（BabyPenguin+Pass3 双钉）。此缺陷同时让 `wait 0` 自旋
  躲过任何停顿诊断（无停靠即无边界），修复后归入 (b) 可诊断。
- [x] M0 收官:BabyPenguin C# 调度器对齐(设计文档 12 章"参考实现必须一起改")——SimScheduler 重写为
  M0 语义:完全收敛 delta(运行队列耗尽即收敛;中途 spawn 当 delta 收养;裸 wait 欠下一 delta 且
  钉住时钟;负数让出重排当前 delta 队尾;条件停靠只等 kick)+ 边界七步(延迟 kick→到期定时器→
  欠账/可运行则同 tick 下一 delta→(C# VM 无 fd)→推进到最早截止点(空 delta 推进段与 mt 编号一致,
  定时器唤醒落在 +2)+ 新退出规则(全部协程结束才退;永久停靠=决策 E 静挂,Sleep(10) 不烧 CPU))
  + 两条停顿皮带(EMPEROR_SCHED_STEP_BUDGET/PIN_BUDGET,退出码 60/61,旋钮与原生同名)。
  停靠类别经侧信道标记传递:extern 紧挨 wait 置标记(_sched_yield/_sched_future_park),
  RunOneDeltaJob 归档时消费(单线程内配对无歧义)。配套 Builtin.penguin:do_wait/do_wait_any
  先 poll 快路径(已就绪同 delta 交付)+ 解析成功记延迟 kick(救援晚停靠者;立即 kick 会在立即
  通道族上造成同 delta 级联消费与停靠重排——实测 A2 被级联重排后 exit 吞掉,mt 无此问题纯因
  其通道延迟提交);wait n 降低到 _wait_ticks(06_SyntaxRewriting,n<0 让出/n==0 裸 wait/
  n>0 定时器条件等);Fifo 背压改条件停靠(裸 wait 会钉死时钟,0.3 节反例);通道族投递/消费/关闭/
  _Fanout 写一律延迟 kick(观测时序=经典轮询;漏 _Fanout 的 kick 曾让端口管线永久停靠);
  LatestChannel.take 的 pending 延迟一 delta 可见(等价 mt 的边界提交,断掉同 delta 端口级联)。
  与 EP-mt 逐字节对齐由 MtSchedulerTest 双钉(MtNegativeWaitYields/MtWaitPinsTick/
  MtBackpressureConditionWait/MtSpawnFutureExit/MtStallBudgetsQuiet + 新增 MtFutureCompletionSameDelta
  (完成唤醒同 delta,经典是 d2));MtFifoImmediateWake 单钉 BabyPenguin(立即通道+粗粒度完成 kick
  =同 delta 交付,mt 延迟 Fifo 是 d2——各自构件语义,互为对照)。停顿皮带用例(60/61)不能双钉
  BabyPenguin(编译/运行阶段坍缩,退出码落在 Compile 段)。
  用例迁移(13.2 审计在 BabyPenguin 新调度器上重做):12 个"静止即退出"用例加显式终止(新退出
  规则下挂起是设计行为,输出两调度器不变);7 个分歧用例转 mt 语义(Compile.Args 加
  --enable-multithread-scheduler,期望按 BabyPenguin==pass1+mt==pass3+mt 重推导:FifoBackpressure
  配对批处理、Passthrough/Catch 的种子竞速(声明默认=可交付种子,poll-first 在 time-0 消费)、
  Queued/OnEvent 的 emit 坍缩、MultiInput 15、FanoutIndependentCursors 线坍缩);3 个事件用例
  节奏化(emit 间裸 wait,期望不变,三端一致);WaitChangePort 拆双钉(BabyPenguin=种子先交付
  "false";WaitChangePortClassic 保 Pass3 经典覆盖)。已知 mt 缺口:pass3-mt 对 WaitChangePort
  形态(第二次等输出端口 hub 槽+定时器写入)永久挂起——记录在案待修。
  教训:测试 runner 只在 Release DLL 缺失时构建 BabyPenguin——Builtin.penguin 是运行期从程序集
  目录装载的数据文件,旧 Release 会让整个矩阵对着旧调度器全绿(已修:源码新于 DLL 即重建)。
- [x] M5 v0 收尾：**分配硬上限压力阀**（设计文档 10.1，M0 后唯一未做的 M5 v0 组成件）——
      `EMPEROR_GC_HARD_CAP`（字节，缺省 8GiB、显式 0 = 不限）；`_emperor_gc_alloc` 分配前
      查 charge 越限 → 触碰者停靠"等回收"（`_emperor_sched_alloc_park`：CO_COND、不计入
      可运行，mt_gc_parked 表）→ 全体分配阻塞即收敛 → 边界强制停世界收集 → 释放停靠者 →
      重试。**确定性 OOM 判定（退出码 62）必须含最小待决 charge**（`_emperor_gc_cap_blocks`，
      停靠表跟踪 min charge）：`活集>上限` 单独不够——实测保活循环恰好停在 cap−48 处
      永久停靠、每次收集零释放（`live` 与 `live+charge` 之间的缝隙）。单次分配>上限立即 62。
      阀门只在 mt 内核下停靠（mt_kernel_seen）；经典调度器/主栈/顺序回退照常分配（上限名义
      值，MtGcHardCapClassicAdvisory 钉字节不变）。教训①：阀门 trip 时不能无条件置
      want_collect——经典路径会退化成每 poll 全量收集（O(n²)，实测挂起）；边界自己强制标志。
      教训②：gc.c 的 8×目标应急收集在并行窗口内联执行会与 worker 变更器竞争（既有隐患），
      现推迟到边界、窗口内增长由上限约束。测试 ×4：MtGcHardCapParkCollect（4 路并行 churn
      穿 256KB 上限、N=4、校验和 1099988 与无上限一致）/ MtGcHardCapOom（62）/
      MtGcHardCapSingleAllocOom（62）/ MtGcHardCapZeroUnlimited（0=不限）；gc_torture
      （ASan）通过。M5 v0 至此全部落地（单堆+分配锁 M4b、边界 STW M4b、压力阀本轮）；
      v1（两堆+移交点复制）是后续专项。

- [x] 已解决（原红哨兵 AsyncSpawnAfterGlobalTouchFails）：根因不是 spawn 综合也不是全局——
      函数名 `pure` 是语法关键字（functionSpecifier），EP 手写解析器把它静默当作函数名，
      而所有调用点 `pure(...)` 词法为关键字、解析成空表达式，在绑定晚期才报莫名其妙的
      E_ASYNC_INVALID。修复：Parser 在函数名位置与 async 调用点对说明符关键字
      （pure/!pure/extern/export/async/!async）报 E_PARSE；类型关键字（double 等）在调用点
      可正常解析、按事实语言保持合法（DynamicLinkTest 有 `fun double` 用例）。
      哨兵改写为 AsyncSpawnKeywordName（负向：关键字为名必须编译失败）+
      AsyncSpawnKeywordRenamed（正向：改名后 STEALABLE/PINNED 判定与运行全对）

- [x] tinyriscv 基线对比（2026-10-04，master worktree 自举 vs 分支，交错 A/B ×10）：
      | 配置 | 中位 | 均值 | 峰值 RSS | 输出 |
      |---|---|---|---|---|
      | master pass3（经典）| 1219ms | 1225ms | 154.0MB | 48614 cycles，逐字节一致 |
      | 分支 pass3（经典）| 1277ms | 1282ms | 154.0MB | 同上 |
      **经典路径 +4.7%**。归因：GC 完全等同（两编译器 1 次全量、13ms、259 万分配/164.6MB
      逐字节相同；gc_span.c 两树一致）；分配锁 ~1%（perf）；其余为运行时代码增长
      （scheduler.c 含全部 mt 内核，单一目标文件静态链入每个经典二进制）与布局效应。
      ——单一分配锁正是 M5 v0 的粗形式，M5 v1（分级锁/每线程 arena）设计目标即收回。
      **mt 形态（--enable-multithread-scheduler）目前跑不了 tinyriscv**：ESL 库按经典
      零延迟线语义设计（Clock 循环每拍只给一个 wait 0 收敛拍），延迟化的线需要更多拍，
      模型在测试台 200 万周期看门狗内到不了程序终点。这是**待迁移项**（esl.Clock 每拍
      增加收敛拍数或管线深度适配延迟语义），不是调度器 bug——迁移后 tinyriscv 才能作为
      mt 形态的 ESL 基准。

- [x] ESL 库 mt 迁移（2026-10-04，解锁 tinyriscv 作为 mt 形态 ESL 基准）——两件事：
      ① **fnext 独立链（根因修复）**：协程单链接字段 qnext 被 spawn 收件箱/运行队列/owed/futures
      四条链表共用；futures 是唯一"成员被单独唤醒"的链表——定时器到期直接唤醒 future 停靠者时，
      运行队列推送写 `co->qnext = NULL` **覆盖了它在 futures 链上的后继**，被定时器唤醒者身后
      停靠的所有协程从链上丢失（count 与 head 失联；ESL 最小探针里模块等沿永不到来、tinyriscv
      2M 周期看门狗超时的根因）。修复：futures 列表改用专用 fnext 字段（push/自摘/kick 全走 fnext）。
      ② **Clock 收敛环**：`__sched_settling()` 探针（mt 实版：信号/Fifo/线注册表 + 延迟 kick +
      运行队列非空；经典 stub 恒 0 保持单拍语义不变）；esl.Clock 改为 `wait 0; while
      (settling > 0) wait 0;` —— 延迟语义下请求-响应线链深度 D 需要 D 拍，commit_all 前等
      静止而非固定拍数。
      **验证**：tinyriscv mt 输出逐字节一致、**48614 周期精确相同**。性能（到 sim-end 行）：
      master 经典 1219ms / 分支经典 1277ms（+4.7%）/ **mt N=1 ~900ms（比 master 快 26%**——
      延迟批量提交红利，与 b3 Fifo 批量 3.4× 同源）/ mt N=4 ~2700ms（ESL 协程全 pin——持有
      Events/线/Regs 不可共享引用——零并行宽度，纯付每 delta 屏障税；预期内的诚实结果）。
      注意：mt 下 sim-end 后决策 E 挂起（模型靠静止退出，需显式 exit——与 BabyPenguin 对齐时
      12 个用例同款处置；tinyriscv 的 main 补 exit 属后续小项）。MtEslClockEdges 哨兵钉住最小形状。
      门禁：MtSchedulerTest 26/26、全矩阵 598/0/1、bootstrap 收敛。

- [x] tinyriscv 显式 exit（2026-10-04）——SimEnd 报告后派生 `uart_drain_exit` 协程：
      测试台约定在最后一个字符停止位发完前就置 x26=1，报告行先于结尾 `\n` 打印（字节序由
      测试钉死）；drain 协程**一次 wait 跳到 tx_end**（逐 tick 等待曾把经典路径拖慢 ~6%——
      经典静止退出本来就是一个定时器跳变），然后 exit(0)。经典/mt-N1/mt-N4 输出逐字节一致。
      顺带修 LspTest/Completion 期望（`__sched_settling` 进补全面——昨晚 ESL 迁移加的存根）。

- [x] **M5 v1 第一级：每线程 span arena + 无锁小对象快速路径**（2026-10-04，设计 §10.2）：
      - gc_span.c `GtArena`（gc_internal.h）：每大小类 current span + 线程本地 quarantine 环
        （隔离环必须跟线程走——边界收集时多个 arena 各持在途 SSA 临时对象）+ 待冲账字节。
        快路径（gt_alloc_small 命中 current span）零锁；span 退役/补充/超块获取走
        gc_alloc_lock——**每 span 一次而非每分配一次**（48B 槽 ~168 次分配/锁）。
      - 堆字节账：单一近似全局 `heap_bytes_total`（sweep 时精确==live；之间由各 arena 慢路径
        冲账；未冲账误差 ≤ arena 当前 span 容量）。**教训**：第一版每分配做 arena 注册表
        RELAXED 求和（goal 检查 + 硬上限前奏各一遍）直接把经典路径拖慢 +2.6%——per-alloc
        读路径必须一次 load。
      - 两个潜在死锁修复（顺带）：慢路径紧急收集、malloc 路径 8× goal 紧急收集都移出
        gc_alloc_lock——finalizer 会分配，锁不可重入（旧代码触发需超块 malloc 失败，未观察到）。
      - 收集器保持**一次停世界扫全部 arena**（sweep/verify/stats 遍历注册表）——本阶段无需
        移交点提升。双域（本地回收器 + 共享堆 + §10.3-10.6）为下一阶段。
      - **-O2 定为 C 运行时默认**（原默认 -O0，分配/调度热路径全栈槽代码，白慢 ~12%）。
      - 性能（tinyriscv 经典，交错 A/B ×10-12，同 .ll 隔离链接）：
        | 配置 | 中位 | vs master 同配置 |
        |---|---|---|
        | master，-O0 运行时（旧默认） | 1222ms | — |
        | 分支 v0 分配锁，-O0 | 1271ms | **+4.0%**（即原 +4.7% 复测） |
        | 分支 arena v2，-O0 | 1292ms | +5.7%（-O0 下 arena 反而略亏：栈槽代码放大额外局部量） |
        | master，OPT=-O2 | 1091ms | — |
        | 分支 v0 锁，-O2 | 1118ms | +2.5% |
        | **分支 arena，-O2（新默认）** | **1103ms** | **+1.1%**（arena 净赚 ~1.4%） |
        余量 ~1% 为 mt 内核链入经典二进制的体积/布局效应。GC_VERIFY 443 次压力收集
        不变量全绿（经典+mt N=4），bootstrap 收敛（-O2 运行时下 pass4/pass5 md5 一致）。

- [x] **M7 free-mt v1（2026-10-04）**：`--enable-free-mt` 装载 scheduler_free.penguin
      （自 mt 策略派生，§6.7/§7.3）——同内核同构件面，边界裁掉虚时钟（无延迟提交可跑、
      无定时器可推进）：owed/runnable → fd 探测 → fd 阻塞 → 退出（决策 E 不变）。
      构件全部**立即化**：Signal 内联提交、Fifo 直入已提交队列、LatestChannel 事务内联、
      Event 投递后立即 kick——写的效应在**同一收敛轮**到达被唤醒读者。`wait n>0` 运行期
      拒绝（exit 3 + stderr 提示）。**AsyncQueue<T>**：MPMC + AtomicBool test-and-set
      自旋锁（竞争退避纯让出，持锁绝不跨停靠；空取者条件停靠，park_begin 先于发布句柄
      ——无丢失唤醒）；ISync → 持有者可偷，EMPEROR_THREADS=4 下生产者消费者真跨线程。
      测试 Tests/FreeMtTest ×5（spawn/join、AsyncQueue ×200 锤 0 故障、立即 Event——
      一次性订阅模型要求两次 emit 间隔一轮，纯让出会把发射者排在订阅者前导致丢事件、
      纯让出 + wait 0 形态、定时器拒绝）。
- [x] **逃逸分析（2026-10-04，§9.2 规则 1 第三形态）**：`new Ownership<T>(l)` 接受
      **线性持有局部变量**——l 的 let 初始化子新鲜（字面量/new/.move()/另一个线性局部，
      按名环防护），且函数体内该名的其它文本使用全部是**接收者位置**（l.push(..)/l.field）
      或 ==/!= 比较——无传参出、无别名绑定、无字段/全局存储、无 spawn/lambda 捕获。
      嵌套块同名 let 一律拒绝（遮蔽歧义）；接收者调用接受为"对被转移对象的变更"
      （经典线性类型折衷，§9.4 记录残余漏洞）。实现：BindBodiesPass 在函数/initial/
      impl 方法体绑定前后设置 model.current_fn_ast_body 上下文，
      SemanticBindExpressions.ownership_* 走 AST。测试 MtOwnershipLinearLocal ×3
      （正/别名拒绝/传参拒绝），MtOwnership 组 8/8。
- [x] **ESL ISync 化（2026-10-04 机器落地；2026-10-06 24ceba1f 根因修复后模型标记重新开启且全 N 验证通过）**：
      - Pass 10 三个放宽（全部闭世界、默认惰性——无标记时行为不变）：
        ① 虚调用：目标接口的**全部实现类** shareable 即干净（泛型基名剥离匹配；
          类接收者判类本身；原始类型接收者干净）；
        ② 纯 extern 白名单：bits/lshift/rshift + 调度停靠协议（_mt_future_park 等
          ——否则每个 wait 位点都 pin）；
        ③ Shareable 类型全局访问干净（§4.3/§10.3 制裁移交；注册表换 _SyncList<ISync>）。
        另修：index_one 补 enum/interface 方法索引（Option.is_none 原本"无体"假阳性）、
        find_fn 单态化回退（模板符号 → mangled 特化体）。
      - mt/free 策略构件跨线程锁（_sched_sync 自旋）：注册表×3、Event 投递/订阅、
        Fifo 交付/消费/写者队列、Latest 读写、Signal 写、SpawnFuture.complete；
        12+2 个构件/未来类打 ISync。嵌套路径用 *_locked 变体防自旋锁重入死锁。
      - **Ram 延迟写**（esl.Mem）：总线写记账（单/周期），时钟边 do_commit 应用——
        修复并行 evaluate 下的立即写竞争（同拍取指会读到半写状态）；Ram 构造子改为
        Ram(clk, words)；RegFile/Reg/Pipe 本就是两段式 ✓；Bus 仲裁探针只读提交态 ✓。
      - **验证结果**：tinyriscv -Wconcurrency 下 4 个模块过程（Fetch/Decode/Execute/
        RegFile）可偷；输出仍逐字节正确（48614 周期）。**但 N=4 ~45% 挂起**——被激活的
        偷取路径暴露 M4b 期预存派发竞态（标记关闭 → 0 可偷位点 → 15 次全过；FreeMt/
        MtParallelFanOut 的纯计算可偷形态 200+ 次锤全过——竞态特定于"偷取 + ESL 构件"
        组合）。**处置：模型/ESL 库级 ISync 标记保持关闭**（已剥离），机器（放宽/锁/
        Ram 日志）全部保留且惰性。调试工具就位：EMPEROR_HANG_WATCHDOG（无收敛转储
        线程态）、-Wconcurrency 深度 dirty 链（virtual on X / extern Y / no body Z）。
- [x] **tinyriscv 多核化 + mt 收益实测（2026-10-04）**：`RiscvCore` 封装（fetch/decode/
      execute/RegFile/私有 ROM+RAM+Bus/可选 UART，core0 独占串口）；main 按 argv[0] 例化
      1..8 核（`__args_get(0)` 是首个用户参数——运行时已剥程序名）；compute 固件（无 R 型
      add 的 ISA 子集：addi 常量步长 + RAM 预置循环数 800+400·id，校验和 5×bound 存
      RAM[0]、done 标志 RAM[1]）；SimEnd 等 core0 x26 且全部 done，打印各核校验和后
      drain-exit。**确定性设计**：每核私有总线 → core0 时序与核数无关（48614 周期恒定，
      单核输出与重构前逐字节一致）。绕过两个工具链缺口：接线审计只认标识符基（链式
      `c.fetch.clk` 报 `?.clk`——提升局部）；比较表达式直接作 bool 参数发射 i1 未升 i8
      （存局部绕过）。
      **顺带修复**：延迟注册表去重从 O(wires²) 线性扫描（8 核 48 线时每次写扫全表——
      mt-N1 多核超线性退化的主因）改为载体侧 O(1) 注册标志（Signal/Fifo/Latest 加
      in_registry 字段 + 接口方法，commit/flush 清除）。
      **mt 收益实测（交错 ×5 中位，ms）**：
      | 核数 | classic | mt-N1 | mt-N4 | mt-N1 vs classic |
      |---|---|---|---|---|
      | 1 | 1140 | 807 | 2667 | **−29.2%** |
      | 2 | 2214 | 1490 | 3670 | **−32.7%** |
      | 4 | 3795 | 4039 | 6205 | +6.4% |
      | 8 | 7574 | 8831 | 10819 | +16.6% |
      （注册表修复前：2 核 −13.5%、4 核 +12.0%、8 核 +44.4%）
      **结论**：延迟批量提交红利在 1-2 核显著且随注册表修复增强；4+ 核 mt-N1 的剩余
      开销 = 延迟往返（pending→commit→kick 比经典直投多 delta）+ 组件锁原子操作（N=1 下
      纯税，可按线程数门控——列为后续）+ future-park 机器。mt-N4 恒亏（模块全 pin，纯
      屏障税）；**8 核 + 可偷模块**的理论估算：8831/4 + 屏障 ≈ 4s < classic 7574ms——
      偷取竞态修复后 mt-N4 在 8 核应可翻盘。测试 TinyRiscvMultiCore（3 核钉死输出）；
      TinyRiscvHelloWorld 单核基线不变。
- [x] **偷取竞态根因修复 + TSan 清零**（2026-10-05，a15fca77 + 85822298，另一会话）：
      根因是 `_co_switch_in` 对 CO_RUNNABLE（status 8）的旧处理"重新归档"——双链 qnext
      截断属主运行队列。修复：status 8 = wake 的推送即重新入队；Mesa 序 do_wait
      （register 占位 → poll → park，就绪则 _mt_future_cancel 撤销幻影条目）；
      destroy 时 mt_unfile_runq_phantom 兜底；between_parked 收敛协议（worker 睡过
      无工作 delta，推送锁内去停泊——修了多核丢 2 UART 字符/提前 7728 周期的真丢失
      唤醒）；裸汇编快速切换（省 swapcontext 每次 2 个 sigprocmask，经典也受益）；
      GC 帧链头 TLS 化（8 线程 CORRUPT REF-MAP）；GC 区域注册表加锁（TSan 抓出的
      真实 soundness bug：并发 add 抢同槽少记 → 协程栈不扫描 → 活对象被扫掉）；
      TSan N=1/2/4 零报告。ESL/模型 ISync 标记已重开（4 可偷位点）。
- [x] **N=8 正确性失效（2026-10-05 复审发现 → 2026-10-06 根因修复）**：
      a15fca77 把 N=8 记为"吞吐墙"，实测为**错结果**（提前结束/丢字符/看门狗超时），
      N=5 起 62% 失败、N=8 100%。定性（证据链见 states.txt）：失效特异于
      **偷取 × ESL 构件**组合（模块钉住 → 12/12 全对；纯计算可偷 → 200+ 锤全过；
      TSan 零报告 = 逻辑竞态；关 GC/快速切换/自旋/between 计数均无效；无协程迁移；
      取指译码流完美，失败 = 时钟冻结 + 零步边界忙活锁，对步数/pin 皮带与
      IDLE_DUMP 全部致盲）。
      **根因（2026-10-06，qlog 环形审计 + gdb 活体解剖定案）——唤醒归档协议的
      五个连环洞**，全部位于"协程已注册、正执行 poll"的窗口与 park 路径：
      ① `mt_switch_away` 不声明状态就挂起：`_sync_begin` 争用让步把 CO_COND 声明
      覆写成 CO_YIELD，恢复后以 RUNNING 挂起 → run_one 状态阶梯 fallthrough 把它
      误归为裸 wait（owed）→ 每轮重排钉死时钟（冻结主形态）；② kick_futures
      无条件消费全部 futures 表项，CO_YIELD 让步中的等待者被消费且唤醒无操作
      → 丢唤醒；③ owed 表与运行队列共享 qnext，双重归档自环链（双 pop 现场实锤）；
      ④ do_wait 的 register→poll→switch_away 中 poll 可让步，让步周期消耗掉
      唤醒的幻影队列项 → park 时两处归档皆已耗尽（NOWHERE 丢模块，时钟空转
      2M 周期跑满 TIMEOUT）；⑤ worker_main 的 between-delta 谓词拒绝在队列非空时
      停泊 → 无痕迹忙自旋烧 8 核（perf/loop_iters 实锤）。
      **修复**：switch_away 在切换点声明 CO_COND（挂起必带 park 态）+ 兜底 owed
      归档；futures 注册幂等化（in_futures 标志）+ do_wait 在 poll 后二次注册；
      kick_futures 分区（只消费+wake CO_COND 态，其余保链）；owed 独立 onext 链 +
      on_owed 防重入守卫；**边界自愈**（每边界把"停泊却无处可寻"的协程修复为
      一次伪唤醒——do_wait 按设计容忍伪唤醒，残余交错从丢失降级为一拍延迟）；
      状态阶梯 fallthrough 变显式不变量断言（exit 66）；between-delta 停泊不再
      看队列。**验证**：N=8 cores=1 12/12 + 20/20 + cores=2/4/8 ×3 全对（校验和
      确定性）、N=1..7 ×3 全对、经典对照全对、bootstrap 收敛；N=8 耗时 4.0s
      （钉住旧版 3.7s）。
      **残余①（UART 丢字符）已修复（2026-10-06 复审）**：根因不在内核——是
      示例层竞态：busy() 只反映仿真时间（tx_end 已过），不保证"打印协程已运行"；
      N≥5 时 drain-exit 与 uart_emit 在同一 tick 于不同线程唤醒，exit(0) 可以
      赢过 print（实测 N=8 复现率 ~50%，非提交声称的 5%）。修复：Uart 增
      tx_done 计数（uart_emit 打印后递增），drain-exit 改等 tx_complete()
      （!busy && tx_done==tx_count）——40 次锤 0 丢字。**残余①'（开放，
      2026-10-07 深挖定性）**：N≥5 + 可偷模块 ~10% 概率报告行差一拍（其余全部
      正确）。证据：(a) 自愈零触发（异常运行 repairs=0——模块从未丢失）；
      (b) 钉住对照 0/30 vs 可偷 3/30——**跨线程唤醒路由特有**（偷取只在首
      delta 分发，之后按决策 A 固定——实质是模块在不同线程的交错）；
      (c) 仪器化即消失（25 连跑零异常）；(d) glock 序修复后形态变为只早一拍
      （48613），比率不变。定性：某模块对某条边的求值落在 tick 边界另一侧
      一拍——正确性不变量全部成立，纯交错确定性；设计 §7.1 本就只承诺边界后
      可靠，N>4 的逐字节周期数不作承诺（文档化而非违约）。**settle 探针 glock
      序修复**（2026-10-07）：__sched_settling 原先先无锁读注册表大小后取
      glock——大小读取在任何 HB 边之前，可读到停靠者推送前的陈旧空值（可证明
      的内存序洞，±1 的部分相关项）；现先 _mt_runnable()（glock acquire）再读
      共享态，mt+free 两策略同步修。② 自愈修复每跑触发约 2800 次（每
      ~16 周期一次，in_futures 陈旧 1 不可达 80% / flag=0 20%）——正确但有每
      ~16 周期一次多余轮询的性能账，协议级根治（每协程唤醒纪元）列为后续；
      ③ 中间误判与证据链保留在 states.txt 与本节历史。
      **历史（误判要记录）**：曾用"撤模型 ISync 标记（模块钉住）"当作处置提交
      （668e29c3），被复审驳回——那是**绕过不是修复**。标记已恢复，本次为真修复。
      **诊断装备（保留）**：EMPEROR_QLOG=1（queued/futures/owed 变更环形审计，
      转储时回放）、EMPEROR_SCHED_CHURN_WINDOW_MS（默认 10000，时钟钉死/爬行
      皮带 → exit 65 转储 settle 分量）、EMPEROR_LOST_SCAN=1（自愈修复的响亮
      模式）、settle 心跳（每 1M 探针一行）、loop_iters（冻结线程的特征）。

- [x] **free-mt GC：poll 升级 STW + 循环回边安全点（2026-10-07）**：
      - **poll 升级**（gc.c + scheduler.c）：want_collect 在并行 delta 内悬挂超过
        EMPEROR_GC_STW_MS（默认 250，0=关）→ 轮询到的协程**复用硬上限阀门的停靠
        协议**（mt_gc_parked 列表 → 强制收敛 → 边界收集 → 解除停靠）——忙服务
        （永续 delta）的堆从"爬到 8GiB 硬上限才收"变为"到达 goal 尺寸即收"。
        实测 FreeMtBusyGc 负载：升级开 fulls=1/RSS 96MB vs 关 fulls=0/RSS 156MB
        （负载越长差距越大，直至 cap）。charge=0 不参与 OOM 判定。
      - **循环回边安全点**（发射器）：while 回边（for 已在语义层降为 while）插
        GC_POLL——无调用的紧循环也能到达 poll（升级与阀门都依赖"每个可运行协程
        最终停靠"）。IR 新指令 IRGcPoll + builder/generator/emitter 三点接线。
        成本：每次回边一次 C 调用（空闲时一次 load + ret）。
      - **连带修潜在死锁**：协程可能在持有组件自旋锁（_sync 区内 Vector.push
        增长分配）时被阀门停靠 → 睡在锁上 → 边界自己的提交路径取同一锁 → 死锁
        （硬上限场景的预存隐患）。修复：TLS 同步深度（_mt_sync_depth_add 包住
        _sync_begin/_sync_end 临界段），阀门与升级停靠遇 depth>0 一律拒绝停靠
        （区域极短，回边 poll 保证后续安全点重试）。
      - 测试：FreeMtBusyGc（4 线程忙服务 + 无调用紧循环 + 4MiB goal，确定性输出；
        fulls>0 与 RSS 对照在提交信息）。门禁：bootstrap 收敛、全矩阵
        1673/0/0/220（+1 新用例）、tinyriscv N8 锤 12 次仅 1 次已知开放项形态
        （丢 'r' + 4416 周期 = 64a73086 known-open 的陈旧 STATUS 面，非回归）。

- [x] **全设置基准重测（2026-10-07，HEAD c62e2316，空闲机，交错 ×5 中位，
      master 基线 bb3ab527 同机重建自举对照）**：
      | 核 | master | classic | mt-N1 | mt-N2 | mt-N4 | 最优 mt vs classic |
      |---|---|---|---|---|---|---|
      | 1 | 1268 | 816 | **566** | 1419 | 2069 | −30.7% |
      | 2 | — | 1317 | **1017** | 2121 | 2925 | −22.8% |
      | 4 | — | 2572 | **2372** | 4026 | 5229 | −7.8% |
      | 8 | — | **4930** | 7036 | 8790 | 15105 | +42.7% |
      - **vs master 单核**：classic 1 核 **−35.6%**（快速切换红利）、mt-N1
        **−55.4%**；2 核起 classic 超.master 单核（+3.9%/+103%/+289%——每核
        工作量线性）；mt-N1 在 4 核时仍比 master 单核占比更低（+87% vs +103%）。
      - **RSS**：mt 各形态 42–48MB（fulls=1、分配 355 万）vs classic 151MB
        （fulls=6、分配 1478 万）——延迟坍缩让 mt 每拍分配量低 4×。
      - **mt-N1 超线性**（8 核 = 自己 1 核的 12.4×）：结构性——收敛 delta 的
        settle 拍数随模块数增长（延迟语义下请求-响应链深 D 需 D 拍，§6.7），
        每拍一次边界往返；profile：run_delta 26.9% + kick/cancel 14.5% +
        LatestChannel take/poll 8.8% + gc_poll 5.9%。
      - **free-mt 正确拒绝 tinyriscv**（wait n>0 → exit 3 + 提示）——周期精确
        模型需要虚拟时钟；free-mt 是无时钟形态（预期行为，非缺陷）。
      - 回边安全点无可测回归（mt-N1 c1 566 vs 上轮 545，+4% 噪声内；c4 持平）。
      - 结论不变：≤4 核 mt-N1 最优；≥8 核 classic 最优；N>1 恒亏；free-mt 适用
        无时钟负载（FreeMtBusyGc）。

## 后续阶段（本计划之外）

- M1 原子量/互斥；M3 微内核逐线程化；M4 多线程 worker + 边界屏障；M5 GC；M6 Signal/延迟 Fifo；
  M2 Pass 10 封闭分析；负数 wait（`wait -1`）降低。
