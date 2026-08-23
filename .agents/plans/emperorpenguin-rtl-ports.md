# EmperorPenguin 实现 RTL Ports 同款功能（含 async/IFuture）— 执行计划

> 2026-08-23 获批。配套设计定稿见 `rtl-ports-design.md`；BabyPenguin 参照实现在 feature/rtl-ports 分支（335/335 全绿）。
> 目标：EP 把端口/通道/调度/async/IFuture 语义编译到原生 LLVM 可执行，与 BabyPenguin 源级兼容（同一测试程序两个编译器都能跑）。

## 侦察确认的关键现状（2026-08-23）
1. **EP 前端**：Lexer 关键字是 if 链（`Lexer.penguin:826`）；`wait`/`async`/`event`/`emit`/`on` 均为**解析后静默丢弃**（bound 层无 case，无声编空）——本计划顺手改为明确报错。`initial` 完整（`emit_main` 顺序 `call`，`LLVMEmitter.penguin:1109-1174`）。
2. **新关键字零冲突**：connect/construct/tick/change/try/catch/input/output 在 EP 源码中无同名标识符（`input` 已改名 source_input，54c53b3）。
3. **后端**：IR 平坦指令表+命名寄存器（无 frame/挂起概念，`IRFunction.penguin:17-121`）；LLVMEmitter 线性走查 + entry alloca（多定义寄存器自动 alloca 化，对挂起友好）；无 invoke/landingpad（无异常机制）。
4. **C 运行时**：gc.c 保守 GC、setjmp 寄存器冲洗（Boehl 技法）、`_emperor_gc_scan_add` 扫描区机制现成；Makefile 加 .c 只改 `SRC` 一行；extern 命名走 universal 规则（`__builtin.foo` → `_emperor_foo`，其余 `<ns>_<name>`）。
5. **EP 自举不受特性影响**：EP 编译器源码不用新语法；pass1（BP 编译的 EP）实现 codegen 后即可编译用户端口程序为原生 exe——**哨兵可先在 Pass1 转绿，无需等自举**。
6. stdlib 注入：`core_builtin.penguin`/`io.penguin` 由 `main.penguin:43-48,70-71` 自动注入用户编译，不在编译器源列表。

## 核心架构决策
1. **协程 = 栈式（stackful）v1**：每协程一块堆栈（ucontext 起步，自有 asm swap 兜底——gc.c 已有 per-arch asm 先例），`wait` = 运行时换栈。
   - 优点：调用树任意深度直接挂起（与 BabyPenguin 栈式语义天然对齐）；emitter 改动极小（wait 就是 extern 调用）；try/catch 用 per-协程 setjmp/longjmp 栈；GC 用现成 scan_add 注册协程栈 + setjmp 冲洗。
   - 缺点（可接受）：每协程 64–256KB 栈；上下文切换百 ns 级。
   - stackless 状态机（设计文档的 aspirational 形态）列为未来优化，不阻塞本计划。
2. **async 语义在栈式下自然成立**：
   - `f()`（async 函数顺序调用）= 直接调用——被调函数 wait 时挂起的是**当前协程整条调用栈**，顺序语义天然正确，无需隐式 wait 改写。
   - `async f()` = 显式 spawn 新协程作业 + 返回 IFuture 句柄。
   - `wait <IFuture>` = park 并登记 waiter，目标协程完成时写结果槽并下一 delta 唤醒。
   - 与 BP 的 SchedulerAddSimpleJob/do_wait 语义对齐；实现更直接（BP 靠轮询，native 靠精确唤醒）。

## 分阶段拆解（每阶段独立可验证，验收即提交）

### E0 — 基线（0.5 天）
`./penguin -b` 收敛确认（tmp/pass2/3 现货）；全矩阵基线留档；EP.Tests BatchBound/BatchLLVM 绿。

### E1 — 前端语法（1–2 天）
- Lexer/Token/Parser 新增：`input/output`（类成员声明，含默认初值）、`connect(a,b);` 语句、`construct {}`（文件级+类内）、`wait <expr> tick`、`wait change(expr)`、`try/catch`。
- **删除 event/emit/on 关键字**（纯 lexer/parser/AST 死代码 + source_file 读取调整，与 BP Stage 5 对齐）。
- wait/async/emit 等未绑定形态从"静默丢弃"改为 `E_NOT_IMPLEMENTED` 明确编译错误（消除现有隐患）。
- AST 节点 + build_text 精确（roundtrip 用）。
- 验收：EP 自举收敛（pass1 编译 EP 源、`-b` 通过）；全矩阵不红；新语法程序在 EP 报清晰错误。

### E2 — bound 层（2–3 天）
- BoundDefinition/Statement/Expression 新变体 + 九 pass 接线（BuildScopes 符号注册、BindBodies/BindExpressions、ValidateControlFlow）。
- PortSymbol + 端口注册表 + **静态拓扑检查**（多驱动/未连接/双驱动 → E_WIRING，对齐 BP de7c859）。
- 脱糖照抄 BP：端口=IChannel 引用字段、connect/y=v/wait 脱糖为 `__builtin` 调用；catch 区间信息进 bound（native 用 setjmp，无需 IR 区间表）。
- **async 绑定**：`async fun` 标记沿用（IsAsync 已在类型层）；`async expr`（含 `async_fun` lambda）绑定到 spawn 脱糖——lambda 走既有 RewriteLambda 闭包机制 + async 标记。
- 验收：EP.Tests BatchBound 新用例；自举收敛；BP 编译 EP 源仍绿。

### E3 — 协程、调度器与 IFuture 核心（C 运行时，4–6 天，最高风险）
- 新 `std/c/scheduler.c`（Makefile SRC 一行）：协程创建/切换、就绪 FIFO、tick 定时器堆、delta 轮循环、静止终止（就绪空+定时器空）、exit 传播、per-协程 try-jmpbuf 栈、**future 对象（pending/running/finished + 结果槽 + waiter 列表）与 spawn/complete/wake**。
- gc.c：协程栈 `_emperor_gc_scan_add` 注册 + 换栈时 setjmp 冲洗；future 与等待者为 GC 对象。
- emit_main 改造：注册 initial 为协程作业 + 调 `_emperor_sched_run()`（无 wait 程序 FIFO 跑完 = 顺序等价，向后兼容）。
- IRGenerator：wait 各形态（tick/bare/0-tick/IFuture）脱糖 extern；`async expr` 脱糖 spawn 调用。
- 验收：TimingTest 5 例 + AsyncTest 核心（WaitTest/WaitAllTest/WaitWithResultTest/ImplicitWaitTest/SpawnAsync 类）在 **EP Pass1** 绿；`-b` 收敛；全矩阵无回归。

### E4 — 通道层 + try/catch native（3–4 天）
- 新 `std/c/channels.c`：Fifo/LatestChannel/MergeChannel/MultiInput/Event C 实现（等待者挂靠、反压停车、per 消费者事务游标、close 级联抛错）。
- `core_builtin.penguin` 增加与 BP `Builtin.penguin` **同名同表面**的类（ISource/ISink/IChannel/IFuture/FutureState/RoutineState + extern 挂 C，universal 命名）——源级兼容合同，**E4 前冻结对照清单**（含 IFuture 的 poll/do_wait 等方法族）。
- try/catch：runtime longjmp 至 innermost jmp_buf；ChannelClosed/WiringError→RuntimeError 对象；未捕获→错误退出 + 输出 flush（对齐 BP b38fe53 行为）。
- 验收：ChannelTest 8 例 + ExceptionTest 5 例 + 其余 AsyncTest（async_fun/lambda 变体）在 EP Pass1 绿。

### E5 — 端口完整语义（2–4 天）
- connect 五源（output hub 扇出/自身 input 穿透/Event 线/变量网线/通道表达式）两汇（端口/MultiInput）脱糖；_Fanout/_LateSource 支撑件（C 实现）；权限矩阵；端口默认初值种子；settle 裸读；wait change。
- 验收：PortTest + ChallengeTest 16 例全部 EP Pass1 绿（设计文档 Q1 典范示例原生跑通）。

### E6 — 收编与全矩阵（1–2 天）
- `./penguin -b` 完整收敛（pass2/3/4 md5 一致）；TimingTest/ChannelTest/ExceptionTest/PortTest/ChallengeTest/AsyncTest 的 Apply To 扩到 Pass1/2/3；全矩阵 4 编译器绿；`Documentation/11_PortsChannelsEvents.md` 补 EP 实现注记（栈式 v1、语句级 construct 为双方共同遗留、epoll 外部事件源为终止规则迁移项）。

## 范围外（明确不做）
- 语句级 construct STW（BP/EP 共同未来项）
- realtime 时间模型（`2s` 挂钟）
- epoll 外部事件源（终止规则 v1 偏差维持文档化状态）
- stackless 状态机优化
- `folk`（两编译器都未实现，维持现状）

## 风险与对策
1. **emit_main 顺序→调度器化的全量回归**：论证——无 wait 程序 FIFO 到完成 = 顺序等价；每阶段全矩阵把关。
2. **E3 最重**：协程切换 asm/栈对齐/GC×future 生命周期——预留最长时间，TimingTest + WaitTest 最小闭环先行。
3. **自举节奏**：E2 起大改后按需 `-b` 收敛验证（耗时），按阶段做不必每提交。
4. **源级兼容漂移**：BP stdlib 表面（类名/方法签名）是合同——E4 前冻结对照清单。
5. **async_fun lambda × EP 闭包机制**：E2 不确定点；若卡壳先保 `async f()` 直接调用形态，lambda 变体单独小阶段跟进。
6. ucontext 弃用告警可接受（自有 asm swap 兜底）。

## 执行约定
ZCode 亲自实现（用户指示不使用 opencode）。每阶段：git 快照 → 实现 → 全量验证（md 套件 + xunit + 按需 `-b`）→ diff 自审 → 提交；任何存量红即回滚修复。进度同步至 /tmp/state.txt。
