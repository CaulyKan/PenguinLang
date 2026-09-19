# 多线程 work-stealing 调度器 —— 准备计划与可行性论证

日期: 2026-09-20
状态: 设计评估（无代码改动），**决策点已确认**（§9）。目标：论证"自动调度 + 值类型可偷 + 引用 pin + 全局分组 + ISync 共享"
方案的可行性，给出修正后的形式化模型、架构与分阶段里程碑。

**已确认的决策（2026-09-20 与用户讨论）**：
- D1 string/深不可变引用算 Shareable（共享格 = ISync ∪ Imm ∪ DeepVal）。
- D2 mut 非 ISync 全局 = 所有权 confinement（触摸即 pin；"每组不同线程"推迟到 Phase 3 组迁移）。
- D3 **sim 下多线程采用 BSP 模型**：全局统一仿真时刻；当前仿真时刻的所有任务全部完成
  （全局 barrier）才推进时间/进入下一轮；轮内并行、轮间串行。语言契约明确：**轮内
  （同一仿真时刻）跨协程读写不可靠**，只有目标性 wait（event/future 就绪）或时间推进后才
  可靠 —— 见 §5.1 论证。通道元素 Shareable 在一切并行形态（sim-mt、free-mt）强制，
  单线程 sim 不限制。
- D4 mt 仅 native 实现（BabyPenguin VM 不做多线程，io stdlib 先例）。
- D5（2026-09-20 第三轮）：**Shareable 格合流到 IValueType**（§3.1 修订）——值类型谓词不再
  引入独立的 DeepVal 概念；显式 `impl ICopy<Self>` 是"边界处调用用户拷贝"的信任挂载点；
  裸 IValueType 含非 Shareable 字段 ⇒ pin + 诊断（永不静默产生竞争）。
- D6（2026-09-20 第三轮）：**`Ownership<T>` 显式 move**（§11.4）取代 freshness 自动推断；
  原则 = **放置自动推断（保守安全），转移显式声明（构造点编译期检查 + 消耗点运行期 trap）**。

## 0. 结论（TL;DR）

方案**本质可行**，且有成熟先例（本质 = Rust 的 `Send` 自动推断 + Pony 的引用能力 +
Erlang/Go 的"仅通过同步原语跨线程"收敛为一个保守静态分析）。但用户方案原表述有
**6 处必须修正**（否则 soundness 有反例）：

1. "创建时分析变量输入"**不充分** —— stackful 协程的后续执行仍可经调用返回值/全局/
   ISync 结果/extern 获得引用；必须做全程序 effect 推断。
2. "仅持有值类型"必须用**独立的深值谓词**，不能复用 `is_value_class`（显式
   `impl IValueType` 带引用字段不会被深查，SemanticClassifyValueTypes.penguin:197-205）。
3. `string` 在分类层是"值类型"（is_type_value_like:356 把所有 primitive 含 string 视为
   value），但在 LLVM 层是 GC 堆指针 —— 它安全靠的是**不可变**，必须以不可变性（Imm）
   作为共享依据。
4. 全局变量"分组到不同线程"**静态不可行**（steal 是运行时事件；两个 global 可运行时
   别名同一对象图，静态不可判定）。修正：所有权 confinement（Phase 1）→ 组迁移（Phase 3+）。
5. spawn/join ABI 自身产生跨线程引用：`async f(args)` 脱糖的 `__SpawnFuture` 被父线程
   poll、子线程 complete —— mt 下必须 ISync 化，且自然导出"**返回类型也必须 Shareable**"。
6. 阻塞原语必须下溯为**调度点**（防 pin 死锁），GC 必须有 **safepoint**（防并发改图 vs
   标记的 premature free）—— mt profile 需要循环回边 poll（Go 式）。

另一个架构级问题：现行调度语义是**确定性 delta-round 离散事件仿真**（虚拟时钟、
quiescence 指纹退出、轮内 FIFO 序），自由 work-stealing 会摧毁轮屏障与全序。解法（D3，
用户提出并经 §5.1 论证确认）：**sim-mt = BSP 式并行仿真轮** —— 轮内并行（同一套
confinement/ISync 机制）、轮界全局 barrier、虚拟时钟只在 barrier 推进；语言契约明确
"轮内（同一仿真时刻）跨协程读写本不可靠"，故轮内并行不破坏任何被承诺的语义。自由
work-stealing（free-mt，无轮无时钟）作为后期激进形态。同一份源码、同一套检查。

## 1. 现状盘点（代码事实，2026-09-20）

| 事实 | 位置 | 含义 |
|---|---|---|
| 协程是 **stackful** 的（ucontext / Win32 fiber，mmap 32MB 栈） | `EmperorPenguin/std/c/scheduler.c:12,71,79` | 偷取 = 整个 `__Coroutine` 句柄搬家，**无需**状态机 lowering（AGENTS.md 的"已知限制"条目已过时） |
| 调度策略层是 **Penguin 代码**（delta-round 循环、队列、定时器、虚拟时钟） | `std/penguin/scheduler.penguin` (`__sched_run`) | mt 调度器可以同样用 Penguin 写在策略层 |
| C 侧只剩"上下文切换微内核"（~15 externs；`sched_ctx` 单全局、单 spawn inbox、fd waiter 表） | `scheduler.c:189` 等 | 单线程假设集中在少数全局态，per-thread 化路径清晰 |
| spawn 面：`async f(args)` 脱糖为 `__SpawnCtx_<n>`（字段=参数）+ `__SpawnFuture`，`_co_spawn_entry(ctx)` | `SemanticBindExpressions.penguin:770-782`，`scheduler.penguin:35-54` | 捕获分析的天然挂接点 = ctx 字段类型 |
| `event/emit/on/folk` 关键字已移除，改 `Event<T>` 类广播 | `scheduler.penguin:1474-1483` | 无历史语法包袱；mt 不需要新语法（符合"用户不关心线程"） |
| 值类型分类**传递**（所有字段 value-like 才是 value class；枚举 payload 递归查） | `SemanticClassifyValueTypes.penguin:340-431` | 深值谓词的基础已在，但见修正 2 的洞 |
| 显式 `impl IValueType` ⇒ 直接判 value，**不查字段** | 同上 `:197-205` | 深值谓词不能复用 `is_value_class` |
| fun-value 闭包恒为引用类型 | 同上 `:187-190` | 捕获 fun value ⇒ pin，天然正确 |
| `string` 分类为 primitive 值类型，但 LLVM 层是 `ref<string>` GC 指针 | 分类 `:356` vs AGENTS.md 类型映射 | 见修正 3 |
| 值语义已落地：值类型在绑定/参数/提取时**拷贝**，mut 只是编译期权限 | 计划 `2026-08-19-value-copy-semantics.md`（四编译器 1130 PASS） | ctx 中深值参数 = 独立拷贝，无别名 ⇒ 跨边界安全的前提已具备 |
| GC：green-tea，保守扫描（主栈 watermark + 协程栈 scan-region + 全局 roots），span 堆 + bitmap sweep，**非移动** | `gc.c`/`gc_span.c`（2096+774 行），`scheduler.c` GC 协议 | 非移动 = 跨线程指针无重定位问题（大礼物）；全部静态单线程，零锁零原子 |
| **原生路径无任何原子操作**（AtomicI64 仅存在于 BabyPenguin VM 的 C# ExternFunctions） | `BabyPenguin/VirtualMachine/ExternFunctions.cs:120-137`；EP std 无匹配 | M1 需从零补原生原子 |
| C runtime 有意避开 winpthread（llvm-mingw 链接约束） | `gc.c:661-663` | Windows 同步原语须走 SRWLock/ConditionVariable |
| 测试套件 byte-exact（含 AsyncTest 30 例的确定性交错输出） | `Tests/AsyncTest/*` | mt 测试必须新的断言模式（顺序不敏感） |

## 2. 目标（用户原则，形式化）

- P1 **自动调度**：用户无 `thread`/`spawn_on` 概念；编译器+运行时决定并行放置。
- P2 **正确性 >> 效率**：宁可全 pin 也不引入一个可能的数据竞争。分析一律保守。
- P3 模型：spawn 默认属于当前线程（affinity）；深值协程可偷（stealable）；持有引用即
  pin；全局变量分组；仅 `ISync` 引用类型可跨线程共享。

## 3. 形式化模型

### 3.1 类型谓词（可从 bound tree 计算，全在 Pass 10；D5 修订版）

```
Sync(T)     : T 的 def 实现 ISync（接口标记，impl IReferenceType + ISync）
Imm(T)      : 深不可变 —— primitive（含 string）；不可变 enum 且所有 payload Imm；
              不可变 class 且所有字段 Imm（含泛型参数不可变）
Shareable(T): Sync(T) ∨ Imm(T) ∨ Own(T) ∨ ValueShare(T)
ValueShare(T): T 是 IValueType 且【所有字段 Shareable】（编译器递归检查，含 Sync/Imm
              字段 —— 持有 Fifo<i64> 字段的值类拷贝共享通道，安全）
Own(T)      : T = Ownership<U>（任意 U，§11.4 —— 构造点独占性已验证）
ICopyCross(T): 显式 impl ICopy<Self> ⇒ 边界处调用用户的 copy() 过界（信任挂载在
              用户亲手写的拷贝代码上；Rust "Clone: Send" 同款）
```

- 跨线程**按引用**共享安全 ⇔ `Sync ∪ Imm`（string 归 Imm —— 不可变堆对象）。
- 跨线程**零拷贝迁移**（协程栈整体偷走 / ctx 字段携带）⇔ `Shareable`。
- 跨线程**按用户拷贝过界** ⇔ `ICopyCross`（值语义与类型处处一致）。
- 接口（IRef）、fun value、裸 IValueType 含非 Shareable 字段 ⇒ ¬Shareable ⇒ **pin**
  （安全回退，非错误）+ `-Wconcurrency` 诊断（"实现 ICopy<Self> 或重构字段以启用偷取"）。
- 不再引入独立的 DeepVal 概念：裸 IValueType 的字段递归检查取代之（原第 2 处修正的洞
  —— 显式 impl IValueType 不查字段 —— 由 Pass 10 自己的字段遍历封堵）。

### 3.2 函数 effect（保守、按可达调用闭包）

```
Effects(f) = touched_globals(f ∪ callees*)   — 静态可达的全局读写集合
           ∪ used_externs(...)               — extern 调用集合
           ∪ {unknown_virtual}               — 接口虚调用/dynlib 未知实现 ⇒ 未知
           ∪ {meta_call}                     — #fun/meta ⇒ 未知（Phase 1 pin）
```

虚调用取"所有已知 impl 的 effects 之并"；跨 dynlib 边界（libmeta 未携带 effects）⇒
unknown ⇒ pin（libmeta v2 可加 effects 字段后放宽）。

### 3.3 Stealable 判定（spawn 点）

```
Stealable(async f(args)) ⇔
    ∀ arg: Shareable(类型(arg))                    — ctx 字段（已是深拷贝）
  ∧ Shareable(返回类型 R)                           — __SpawnFuture 跨线程交付
  ∧ Effects(f) 的 globals 全部 Imm ∨ Sync
  ∧ used_externs ⊆ 白名单（纯/C11 原子/线程安全 IO）
  ∧ 无 unknown_virtual ∧ 无 meta_call
```

不满足 ⇒ **pin**（合法，只是不并行）——pin 不是错误，是保守回退。

### 3.4 域不变式（soundness 目标）

- **INV1（ confinement）**：任一非 Sync 对象的可变访问只发生在其出生域的单个 OS 线程
  执行流中。推导链：持非 Sync 引用的协程 ⇒ Stealable=false ⇒ 永在出生线程；其 spawn 的
  child 默认 pin 同线程 ⇒ 传递闭包成立。
- **INV2（跨线程流）**：线程间引用传递只经 Sync 对象内部（含其方法收发 Shareable 值）
  或 Imm 对象。
- **INV3（全局）**：mut 非 Sync global 只被 owner 域触摸（Phase 1: owner = 初始化域
  =main）；Imm global 任意读；Sync global 任意用。
- **INV4（GC）**：全局堆非移动；标记在 STW safepoint 进行（switch-in / park / alloc
  slow-path / mt 循环回边 poll）。

### 3.5 证明骨架（对调度转移的归纳）

初始：init 单线程，INV 平凡成立。归纳各转移：
(a) 本地执行——对象只在出生域被触摸（Stealable 保守性 ⇒ 持引用者不换域）；
(b) steal——只偷 Stealable 协程，携带状态 Shareable（INV2），可达 globals 全 Imm/Sync
（INV3）；(c) 跨线程消息——channel 是 Sync、元素 Shareable（编译期强制，mt profile）；
(d) 分配——新对象域=当前线程；(e) GC——STW + 全根枚举（每线程主栈 + 全部协程栈
region + global roots + Sync roots）⇒ 存活性完备，非移动 ⇒ 无指针失效。
∴ 无数据竞争（非 Sync 可变访问单线程化）+ 无 GC 内存错误。
**不承诺**（Go 先例）：执行顺序确定性、免死锁（Sync 误用仍可死锁，debug 检测器缓解）、
免饥饿/活锁。

### 3.6 先例对照

| 系统 | 对应物 | 差异 |
|---|---|---|
| Rust `Send` | Stealable/Shareable | 我们是**推断**不要求用户写；无 uniqueness（故通道只许 Shareable，Phase 2+ 加 freshness 移动） |
| Pony ref caps（iso/val/tag） | DeepVal/Imm/Sync 三分 | Pony 编进类型系统每个表达式；我们只在 spawn/边界点查，粒度粗但便宜 |
| Erlang / Go | 仅经 channel/同步原语跨线程 | 我们多了静态 confinement 免数据竞争，Go 靠 runtime 检查+纪律 |
| Java inline threads (Loom) + immutable | Imm 共享 | 我们编译期拒绝而非运行时警告 |

## 4. 用户方案的 6 处修正（每处：反例 → 修正）

1. **仅分析创建时输入不充分**。反例：`async worker() { let l = get_list(); l.push(1); }`
   零捕获，但 `get_list()` 返回全局 List 引用。修正：Stealable 必须覆盖身体的全可达
   调用闭包（§3.2/3.3）。
2. **深值判定不能用 is_value_class**。反例：`class Holder { l: mut List<i64>; impl IValueType; }`
   被分类为 value class（`:197-205` 不查字段），实例可内联在栈上且携带堆引用。修正：
   独立 DeepVal 传递谓词。另：值拷贝语义（2026-08-19）保证 ctx 里深值参数无别名 —— 前提
   已具备。
3. **string 的共享依据是不可变不是值**。分类层 string=value primitive，运行层=GC 堆指针。
   修正：Imm 类（string 为成员），共享语义=多线程只读；需一次性审计无字符串原地突变
   （现有 IStringOps 全只读、concat 均新建 —— 已符合）。
4. **全局"分组到线程"静态不可行**。(i) steal 运行时才发生，静态无法绑定 global↔线程；
   (ii) 两个 global 可运行时指向同一对象图（静态别名不可判定）⇒ 分组必须是**对象图连通
   分量**而非变量表。修正（Phase 1）：所有权 confinement —— mut 非 Sync global 归
   初始化域，触摸即 pin；Imm 全局共享读；Sync 全局共享。Phase 3+ 以"组迁移"（组静默 +
   整体过户 + 附带 pinned 协程搬家）实现"每组可到不同线程"的本意。
5. **spawn/join ABI 自身跨线程**。`__SpawnFuture`：父线程 `poll`、子线程 `complete` ——
   天然共享可变对象。修正：mt 下 future 内部 ISync 化（atomic done + Shareable 结果槽），
   并导出规则"返回类型必须 Shareable 才可偷"。`__SpawnCtx` 在 spawn 后仅 fut 逃逸出
   语句块（脱糖 `:772-774` 已确认），ctx 本体可视为过户给子协程。
6. **阻塞原语与 GC 的两个陷阱**。(i) OS 级 mutex.lock 会与 pin 组合死锁（持锁协程排在
   同线程队列）⇒ Sync 阻塞操作必须 park 协程而非阻塞线程（调度点语义）；(ii) 纯 CPU
   不分配不 park 的协程使 STW 无法停它，而它并发改图会让标记漏边 ⇒ premature free。
   修正：mt profile 发射循环回边 safepoint poll（读全局 epoch，~1 load/迭代，Go 同款）。

## 5. 与 delta-round 仿真语义的关系：三形态（D3）

用户确认的语言契约：**同一仿真时刻（delta 轮）内跨协程读写理论上不可靠**；只有目标性
wait（event/future/channel 就绪）或仿真时间推进后，读取才可靠。这恰是 SystemC 的立场
（delta 内进程求值顺序 unspecified），使**轮内并行**成为合法优化：

### 5.1 sim-mt（BSP 式并行仿真轮）可行性论证

模型 = Bulk Synchronous Parallel：superstep = delta 轮。轮开始快照就绪集 → 多线程并行
执行（work stealing，仅偷 Stealable 协程）→ **全局 barrier**（本轮全部协程跑完或 park）
→ 单线程 barrier 段做策略决策（轮转队列、定时器触发、时钟推进、fd 轮询、quiescence
指纹）→ 下一轮。逐项核对现有语义：

| 现有机制 | sim-mt 下的处理 | 是否保住 |
|---|---|---|
| 轮内 FIFO 序 | 变为 unspecified（契约已允许；SystemC 同款） | 语义合法，但测试需审计（§8） |
| 统一虚拟时钟 | `_sim_now_tick` 只在 barrier 段写；轮内只读（barrier 的 release/acquire 边保证可见性） | ✅ |
| 时钟推进决策 | 本就是"无进展轮"的 barrier 决策，天然全局 | ✅ |
| 同轮 spawn 归队 | 中途 spawn 按亲和性进当前线程队列（保持"spawn 当轮可跑"） | ✅ |
| `_sim_activity` 计数 | atomic fetch_add（轮内多线程递增） | ✅ |
| 定时器表注册 | 轮内 `_after(n)` → 每线程暂存，barrier 按 (deadline, spawn seq) 确定性归并 | ✅ |
| fd 停靠/轮询 | `_fd_poll` 只在 barrier 段（单线程）；waiter 注册每线程暂存 | ✅ |
| quiescence 指纹 | barrier 时刻全体已 park（栈冻结）→ 单线程按**全局 spawn 序号**归并的收养序迭代 —— 收养序仍确定性 | ✅ |
| LatestChannel 同轮坍缩 | "同轮多写坍缩到最后值"的"最后"变 nondeterministic（契约允许：同刻写入本不可靠） | 语义合法 |
| GC | **barrier 即天然 STW safepoint**（世界在轮界整体静止）→ 轮界收集；轮内并发分配需 per-size-class 锁/每线程配额；不分配不 park 的死循环行为与今天一致（hang，无回归）——**free-mt 才需要循环回边 poll** | ✅ 且大幅降低 M5 难度 |
| confinement | 与 free-mt 完全同一套 Shareable/Stealable 分析与 INV1-4（同轮两协程可能在不同线程 → 通道/Event 内部必须 Sync，元素 Shareable 在并行形态强制） | ✅ |

结论：在"全局统一仿真时刻 + 轮内全完成才推进"约束下，**sim 可以多线程**，且跨轮语义
（时序、quiescence、tick 顺序）保持确定性 —— 不确定的只有轮内交错，而这在语言契约里
本就不可靠。性能特征为 BSP 型：轮大（多进程/轮，ESL 典型负载）收益好；细粒度反应式
流水线受 barrier 开销限制（~µs 级/轮），这正是后续 free-mt 形态的存在理由。

### 5.2 三形态总表

| | sim（默认，现状） | sim-mt（BSP，新） | free-mt（后期） |
|---|---|---|---|
| 线程 | 1 | N（轮内并行 + 轮界 barrier） | N（自由 work stealing） |
| 时间 | 虚拟时钟、delta 轮 | 虚拟时钟、delta 轮（不变） | 墙钟；裸 `wait;` = 让出 |
| 确定性 | 全确定 | 跨轮确定、轮内 unspecified | 仅免数据竞争 |
| channel/Event | 现实现 | 内部 Sync + 元素 Shareable | 同 sim-mt |
| GC | 现状 | 轮界 STW + 分配锁 | 循环回边 safepoint + STW |
| safepoint | 不需要 | barrier 即 safepoint | 循环回边 poll |
| 用途 | ESL 建模、byte-exact 测试、LSP、自举 | 用户程序并行加速（保守版） | 高吞吐服务器类（激进版） |

同一份源码、同一套 confinement 分析与 ISync 库；编译器自身**永远 sim**（md5 收敛不受
影响）。stdlib：`scheduler_mt.penguin` 仅并行形态自动加载（io stdlib 先例）+
`#if (#option("mt"))` 门控共享部分。staging 上 **sim-mt 先行**（复用 delta-round 循环
结构，语义风险最小），free-mt 是其策略变体（去 barrier、去虚拟时钟）。

## 6. 架构与组件

### 6.1 ISync 契约（新接口，`std.sync`）

```
interface ISync { impl IReferenceType; }   // 标记接口
```
契约：方法可从任意线程调；内部自同步；**所含值必须 Shareable**（编译期在 mt 强制，
含泛型参数约束）；阻塞型方法 park 协程不阻塞线程。首批评审对象：`__SpawnFuture`、
`Fifo`、`LatestChannel`、`Event`、`_Fanout`、`MultiInput`（mt 形态），新增
`std.sync.AtomicI64/AtomicBool`、`std.sync.Mutex`、`std.sync.AsyncQueue<T>`（`T: Shareable`）。

### 6.2 编译器：Pass 10 ConcurrencyAnalysis（新 `src/bound/SemanticConcurrency.penguin`）

输入：pass 7 分类后的 bound tree（含 libmeta 声明）。输出：每 def 的
`is_shareable`、每函数 effects、每 spawn 点 Stealable + pin 原因链（诊断
`-Wconcurrency`："pinned: arg 1 `List<T>` not shareable" / "pinned: touches global `x`"）。
加入 `EmperorPenguinPass1/2/Lib.penguins` 三处源集。mt profile 下非法跨域流
（如非 Shareable 通道元素）为编译错误。meta 反射挂钩：`#is_shareable(t)`、
`#is_stealable(site)` 供测试断言。

### 6.3 spawn ABI（mt 分支）

`__SpawnCtx` 增 `stealable: bool` 常量字段（Pass 10 回填）；`_co_spawn_entry` 增
mt 变体 `_mt_spawn_entry(entry, stealable)` 把句柄放入**本线程** deque（stealable 才
进可偷区）。`__SpawnFuture` mt 形态：C11 atomic done-flag + Shareable 结果槽 +
父线程 park 队列（`wait fut` 在 mt 下 park 到 done，不再轮询 delta 轮）。

### 6.4 C 微内核（scheduler.c + core_builtin.c）

- per-thread：调度上下文（`sched_ctx` → per-thread 数组）、spawn inbox、主栈 watermark、
  run deque（Phase 1 互斥锁版，正确优先；Chase-Lev 后置）。
- 新 extern 族 `_mt_*`：atomic 族（AtomicI64/Bool/U64，C11 `__atomic` / Win Interlocked）、
  mutex（pthread / SRWLock，**不引入 winpthread**）、park/wake（futex 式：park-on-address +
  wake-one/all，POSIX futex / Win WaitOnAddress）。
- fd 轮询归属单线程（designated poller），事件分发唤醒对应域。
- 句柄生命周期：destroy 只由 owner 域执行（或 epoch 化延迟回收）。
- print 加锁（行原子性）；`__object_id` 原子化。

### 6.5 GC（分形态，均最保守正确起步）

- **sim-mt**：barrier 即天然 STP/STW（世界在轮界整体静止）→ 分配压力检查放 barrier 段，
  轮界收集（每线程主栈 watermark 协议照抄现有 main 协议；协程栈 region 跟句柄走）。
  轮内并发分配：per-size-class 互斥起步（Phase 2 每线程批量取槽）。轮内分配越过硬上限
  → 该线程 park 等 barrier（其余线程继续；轮界收集后恢复）。**不需要循环 safepoint poll**
  —— 不 park 不分配的死循环与今天单线程行为一致（hang，无回归）。
- **free-mt**：循环回边 safepoint poll（读全局 epoch，~1 load/迭代）+ park/alloc slow-path
  检查 + STW 标记；非移动性保持（span bitmap sweep / wholesale recycle / malloc 大对象
  路径均不搬迁对象）。

### 6.6 并行调度策略层（Penguin，`scheduler_mt.penguin`）

**sim-mt（先行）**：保留 `__sched_run` 的 delta-round 循环骨架，"执行就绪队列"一步换成
C extern `_mt_run_round(队列快照)`：N 线程并行消费就绪集（各自 deque + 偷 Stealable），
全 park/finish 后到 barrier 返回；策略代码（队列轮转、定时器、时钟、quiescence）留在
Penguin 层、只在 barrier 段执行 —— 改动集中、策略逻辑与今天 1:1。spawn 中途归队按
亲和性进当前线程队列；收养序以全局 spawn 序号确定性归并（quiescence 指纹迭代序依赖它）。
**free-mt（后期）**：去 barrier 去虚拟时钟的自由 work-stealing 变体（N-1 worker + main
协同，空闲退避偷取，全局空闲屏障退出，`exit()` 广播停机）。两形态共享偷取内核与
confinement 机制。

## 7. 里程碑

| # | 内容 | 验收 |
|---|---|---|
| M0 | 本计划 + 决策点确认（§9 已确认 D1-D4） | ✅ 2026-09-20 |
| M1 | 原生原子/Mutex/park-wake 原语（C + `__builtin` extern + `std.sync` 单线程可用） | 单线程 .md 测试（Atomic CAS 语义等 BabyPenguin） |
| M2 | Pass 10 分析（Shareable/effects/Stealable + 诊断 + meta 反射） | sim 全矩阵不回归；新增 ConcurrencyAnalysis 单测；pin 原因链可断言 |
| M3 | C 微内核 per-thread 化（sched_ctx/inbox/watermark/deque/句柄归属） | sim 全矩阵不回归（此步无行为变化）；stress 单测 |
| M4 | **sim-mt BSP 调度器**：`_mt_run_round` 并行轮执行 + barrier；通道族**条件 Sync 化**（#specializing 按 `Shareable(T)` 分叉，§11.3）；spawn ABI（stealable 标记 + future 条件 Sync 化）；`--threads=N`（N=1 退化为今天） | mt N=1 输出与 sim 逐字节一致；N>1 集合相等；AsyncTest 顺序依赖审计完成 |
| M5 | GC 并行化：**v0** = 单堆 + per-size-class 分配锁 + 轮界 STW（正确性基线，轮界任意分发即安全）；**v1** = 分配类路由（TLS 上下文模式选 arena；Sync/Imm 静态路由共享池）+ 双收集器（§11.2） | gc-torture/gc-bench 并行模式；TSAN 构建零报告 |
| M6 | std 公开 API（Atomic/Mutex/AsyncQueue）+ free-mt 形态（循环 safepoint poll、自由偷取）+ 文档（docs/en+zh 双树新页）+ `Tests/MtTest/` + runner SORTED 断言 + CI | 全绿；性能基准报告（sim vs sim-mt 加速比） |

每里程碑独立分支 + 提交（AGENTS.md 工作流）；M2/M3 可并行推进。free-mt 拆出独立里程碑
后，M4-M5 不再需要循环 safepoint（barrier 即 safepoint），编译器侧侵入性显著下降。

## 8. 测试与验证策略

- **AsyncTest 顺序依赖审计（M4 前置）**：逐例检查 30 个现有异步测试的期望输出是否依赖
  轮内 FIFO 序（同轮多协程 print 顺序、同轮多写通道的接收顺序）；依赖者改写为轮序稳定
  断言或显式时间推进（wait n tick）后断言。这是 sim-mt 合法性的实证部分。
- runner 扩展：`ExpectedStdout: SORTED`（行集合相等）；`--threads N` 路由；并行类别
  Apply To = pass3/release。
- 差分测试：同程序 sim N=1 vs sim-mt N=1 逐字节相等；sim-mt N>1 集合相等 + 无竞争。
- TSAN（clang `-fsanitize=thread`）debug 链路进 CI 周期任务；Windows 用 WINE 冒烟。
- 压测：对拍随机任务图（spawn 深度/通道星型/全局触摸模式），TSAN + 断言域标签。
- debug 模式运行时防御：对象头域标签抽查（metadata 偏移 0 有空间），跨域流即 trap ——
  静态分析的纵深防线。

## 9. 决策点（已确认 2026-09-20）

1. ✅ **string/不可变引用算 Shareable**（D1）：共享格 = ISync ∪ Imm ∪ DeepVal；M1 附带
   一次性审计确认无字符串原地突变（IStringOps 现全只读，初步符合）。
2. ✅ **mut 非 Sync 全局 = 所有权 confinement**（D2）：触摸即 pin；Imm 全局共享只读；
   "每组不同线程"推迟到 Phase 3 组迁移。
3. ✅ **sim 下多线程 = BSP 模型**（D3，用户提出、§5.1 论证通过）：全局统一仿真时刻，
   轮内全任务完成才全局推进；轮内并行采用与 free-mt 同一套 confinement/ISync；语言契约
   明文化"轮内跨协程读写不可靠"。通道元素 Shareable 在一切并行形态强制，单线程 sim
   不限制。
4. ✅ **native-only**（D4）：BabyPenguin VM 不实现多线程（io stdlib 先例）；跨编译器
   一致性由 sim N=1 保证；无新语法（spawn 面保持 `async f(args)` + `wait` 形式）。

## 10. 风险清单

- GC 并发化审查规模大（~2.9k 行 C 全静态单线程）——M5 独立里程碑 + torture 套件兜底。
- scheduler.c per-thread 化是侵入式改动，sim 回归风险 —— M3 全矩阵门禁。
- 确定性测试与 mt 的类别隔离（runner Apply To 路由必须精确）。
- Windows：每线程 fiber 化 + SRWLock/WaitOnAddress（避免 winpthread 的既有约束）。
- dynlib/libmeta 不携带 effects ⇒ 引用库的 spawn 全 pin（可接受，v2 加字段）。
- 自举不受影响（编译器恒 sim），但 stdlib 双形态编译需防 pass1 语法面回退。
- 性能保守起步（锁化 deque/分配），先正确后优化，符合 P2。

## 11. 优化项评审（2026-09-20 第二轮，已并入里程碑）

### 11.1 不可变引用提升（freeze）——采纳静态子集，挂起动态版

朴素版（"观察到无人持有可变引用即共享"）不成立：(i) mutability 是 per-binding 权限而非
对象状态，`List<mut T>` 等元素类型提供内部可变性通路；(ii) "mut 持有协程退出"不清除其
导出的引用（全局/通道/共享对象/子协程 ctx 中全部存活），存活别名集合运行时不可枚举；
(iii) 无机制阻止未来产生新的可变别名。**采纳的重构（D5/D6 修订）**：
- (a) 静态 `Imm(T)`（§3.1）；
- (b) ~~freshness/move 自动推断~~ → **`Ownership<T>` 显式 move**（§11.4，D6）：构造点
  编译期验证独占性（new / move 结果 / 线性持有局部），消耗点运行期 trap；通道/spawn 把
  `Ownership<T>` 视为合法跨界元素（任意 U）。原则：放置自动推断（保守安全），转移显式
  声明（可检查）；
- (c) 动态 freeze 位 + 写路径检查：维持 defer（并行构建每次堆字段 store 加载+分支
  ~5–15% 税，与 (b) 覆盖重叠；Imm 图冻结使其提升无传递风暴，是未来重启的理由）。

### 11.2 per-thread GC + 全局 GC——采纳（M5 v1），并借此封堵一个既有漏洞

**契合**：双堆设计最贵的"shared→local 指针不可出现"写屏障在 confinement 下免费成立
（INV2 + ISync 元素强制 Shareable ⇒ 共享对象结构上不可能指向线程局部对象；局部 GC 把
共享类对象当不透明哨兵跳过即可，无需穿透）。
**封堵的漏洞（v1 的前提）**：偷取**已驻留的继续体**会携带其在原线程分配的局部对象 ⇒
原线程局部 GC 失去根、新线程不扫原 span ⇒ UAF。修复 = **分配类路由**：每线程双 arena
（局部/共享），分配按 TLS 当前执行上下文选（pinned→局部，stealable→共享），静态
Sync/Imm 类型恒路由共享池 ⇒ stealable 协程全部分配天然共享类，驻留偷取安全；pinned
永不迁移。保守替代（不推荐）：steal-at-spawn-only（继续体不换线程，牺牲 sim-mt 轮界
分发自由度）。**代价（string/Imm 的高频分配）与 v1 完整不变式集**：
- **Imm 惰性提升**（v1.1）：Imm 对象默认进当前 arena（pinned→本地），只在封闭移交点集合
  （Sync 通道写 / spawn ctx 填充 / Ownership 移交 / 全局初始化）传递提升为共享类。
  Imm 的**子节点集合在构造时冻结** ⇒ 提升后不可能经 store 获得本地孩子 ⇒ 共享-Imm
  永不指向本地 ⇒ **零写屏障**（字符串扁平，提升 O(1)）。反例场景：字符串密集循环
  （to_string/append）的短命垃圾若静态路由共享池会把全局 GC 频率推高一个量级——惰性
  提升使其生死于本地堆。
- **可变图无此性质**：共享类可变容器被接收方 store 新本地引用即砸穿不变式（经典写屏障
  场景）。v1 规避：语言层面**不存在**"共享类可变对象 + 普通 store"组合——
  (i) Sync 对象及内部节点恒共享池，一切变更发生在已持锁的 Sync 方法内（锁内提升/分配）；
  (ii) Ownership 跨域 move 走 **copy-on-foreign-receive**：接收侧比对 home 域，同域零拷贝
  （confined 流水线常态），异域深拷贝落进接收方 arena（拷贝时独占 ⇒ 无竞态；原图留在
  发送方本地堆由其本地 GC 回收；深拷贝带环检测 O(图)）；跨域大 buffer ≈ 一次 memcpy，
  通常远小于其上计算量；零拷贝+写屏障替代方案留待实测。
- (iii) stealable 执行分配恒共享池（Ownership 接收拷贝落在接收上下文的共享模式 arena）。
- v0 单堆下全部不存在（唯一收集器全根枚举，Ownership 零拷贝直接安全）——再次确认
  v0→v1 staging。
v0（单堆+锁+轮界 STW）无此问题（唯一收集器在 barrier 全根枚举），故 v0 先行。

### 11.3 #specializing 条件 ISync——采纳（M4），三项中性价比最高

`Fifo<RefT>`（T 非 Shareable）不实现 ISync ⇒ 类型非 Shareable ⇒ 不能经任何跨线程流
传递 ⇒ 只能经 spawn 参数（接收者 pin 同线程）或本域全部分发 ⇒ **两端自动同域**——
这是 confinement 分析的自然推论，零新增强制机制。实现：Pass 10 的 `Shareable(T)` 为
唯一事实源，#specializing 按谓词分叉注入（Shareable T → 锁+park 背压 + ISync vtable；
非 Shareable T → 现无锁实现），vtable 随单态化实例变化是 #specializing 本职。注意：
(i) 谓词与注入实现必须同源生成（coherence）；泛型接口（ISink/IChannel）的 Shareable 性
随参数走（std 声明"Sync iff 参数 Shareable"由 Pass 10 消费）；(ii) 静默 pin 需
`-Wconcurrency` 诊断提示；(iii) `__SpawnFuture<R>` 同理条件化（pinned spawn 用今天的
廉价 future）。与 11.4 组成两级故事：默认同域快路径（无锁零拷贝）+ 显式跨域路径
（Shareable/Sync 或 Ownership move）。

### 11.4 Ownership<T> 显式 move（D6，M6 实现，v0 零拷贝 / v1 copy-on-foreign-receive）

```
enum Ownership<T> { owned: T; not_owned; }
fun owned(v)     // 构造：编译期验证独占性
fun move(mut this) -> T   // 消耗：置 not_owned；对 not_owned 再 move → 运行期 trap
```

- **构造检查（规则锚点，编译期，函数内可判定）**：`Ownership.owned(v)` 只接受
  (i) `new` 表达式；(ii) 刚从另一 Ownership `.move()` 解出的值；(iii) **线性持有的局部**
  ——从出生（new/move 结果/参数起点）到包装点：未逃逸进任何字段/全局/通道/普通调用
  实参、无别名绑定、未被取地址。违规 ⇒ 编译错误。跨阶段转发链天然成立（上游收到的
  owned 解包后再包装，起点仍是 move 结果）。
- **跨线程护照**：通道/spawn 将 `Ownership<T>` 视为合法跨界元素，任意 U —— 独占性已由
  构造检查保证。
- **编译器特殊处理（payload 私有，防泄漏）**：`Ownership<T>` 是编译器内建特殊类型（同
  Box 的待遇级别）。payload 槽 `.owned` 的**读和写**一律在 binder 拒绝（错误信息指向
  `.move()`）——否则 `let a = foo.owned;` 直接漏出引用，独占性作废。`is
  Ownership<T>.owned` 纯标签测试允许（不解包）。wrapper 本体可自由传递/存储。泄漏路径
  枚举封堵：直接成员访问（读/写）、meta/反射合成的成员访问（同一 binder 路径拒绝）、
  模式匹配解绑（PenguinLang 的 is 不解包，天然无路）。局部 `t = o.move()` 解包后 t 是
  普通局部，可正常使用；要跨线程必须重新 `Ownership.owned(t)` 包装（t 自 move 起重新
  纳入线性持有检查）。
- **GC 对齐**：移交点 = 提升点。v0 单堆零拷贝直接安全；v1 下跨域走 copy-on-foreign-
  receive（§11.2），同域零拷贝。**walk/拷贝发生在只有发送者（v1 拷贝时只有接收者）能
  碰图的时刻 —— 所有权语义与 GC 完整性在同一点闭合，无锁无屏障**。
- **原则**：放置（placement）自动推断（错误只损失并行度，保守安全）；转移（transfer）
  显式声明（错误才是数据竞争，必须可检查）。取消原 freshness 自动推断。
