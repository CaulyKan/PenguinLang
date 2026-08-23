# RTL-style Ports & Connect — 设计定稿（2026-08-21 grill 会话）

## 动机

模块间通信策略可组合：直连 wire / 防丢 FIFO / 满丢弃 / 满反压 / 多输入多输出路由（MQ 风格）。
模块体对下游策略无感知，策略在接线端（construct）决定。

## 语义决定（13 轮拷问结论）

### 1. 传播语义：Delta-cycle 结算
- 端口读是 settle point：读端口的表达式先让出调度，当前时刻所有传播结算完毕后取值。
- 端口 = 官方跨例程数据通路，取代文档里"不能用 wait 0 等别人的赋值，要用 event"的警告。
- 普通变量读保持既有 next-tick 语义（`Documentation/08_TimingModel.md`）；**端口读自动 settle vs 普通读不 settle 的不对称必须写进文档**。

### 2. 权限矩阵（RTL 严格）
- `input`：模块内只读（settle point）；模块外不可读写；唯一驱动路径 = connect。
- `output`：模块内 `write(v)` / `try_write(v)`（`y = x` 对 output 解糖为 `write(x)`）；模块外只读。
- 多驱动 output / 写自己 input / 写别人 output / 外部读 input = 编译错误。
- 端口不携带 `mut` 修饰符（矩阵已穷尽读写规则）。
- `force`/`deposit`（testbench 后门）= 未来 stdlib 函数，非语法。

### 3. 连接与初值
- 未连接 `input` = 编译错误，除非带默认值 `input x: i32 = 0;`；未连接 `output` 合法（最多 lint）。
- 端口初值 = 显式默认或类型零值 → time-0 结算确定。
- 连接时刻的初值算一次交付：time-0 每个模块的首次 `wait` 立即唤醒。

### 4. 交付模型：事务式，不做值去重
- `input` = value + event 组合体，含"已交付值槽位"；`wait x` 推进槽位并返回交付值；裸读 = 读当前槽位（settle point）。
- **每次 write 都是一次事务：写相同值也交付（与 Verilog 不同，无值比较去重）**。
- wire 策略：槽位 latest-wins —— 两次写之间下游未醒则只见终值（要防丢用 FIFO，这正是 FIFO 的存在意义）；同一 delta 内多次写合并为一次唤醒。
- FIFO 策略：每值必达，顺序保留。

### 5. 词汇表：删 event/emit/on，删 poll
- `event`/`emit`/`on` 三关键字全删；`Event<T>` 内建 class（first-class 值：构造传参、全局量、字段皆可）→ 闭合匿名广播与自由函数 emit 的缺口（免端口钻孔）。
- 定时器/条件 = `wait 1s` / `wait a == 5`（既有语法，`07`/`08` 文档已有）。
- 订阅模式 = `initial { while (true) { let v : T = wait X; ... } }`。
- **`wait` 是唯一唤醒原语**（删 poll）：接受时长 / 条件 / Event 对象 / 自己的 input / 别人的 output，凡带载荷者返回交付值。

### 6. 通道架构：通道是 first-class 值
- `ISource<T>` / `ISink<T>` 接口对；通道 = 同时实现两者的普通对象（Fifo / Event / Router / 用户自定义）。
- `connect` 多态：源 {`mut` 变量（隐式线网）、output 端口、通道} → 汇 {input 端口、通道}。
- 直连 = wire 语义；插通道 = 两段 connect 经过通道对象（可传引用、可观察深度、可 drain）。
- 反压 / 丢弃 / 路由 / 仲裁全部是库代码；**编译器只管三样：端口声明、connect 拓扑检查（多驱动编译错）、调度交付原语**。
- `connect` 仅在 construct 块内合法（拓扑静态可查）。表达式源（`connect(x+1, ...)` 的 Verilog assign 风格）推迟。

### 7. construct 块（顶层）
- 块内 `let` 提升为外层作用域普通绑定（与既有顶层 let 同命名空间，冲突 = 编译错），零新作用域规则。
- 所有 construct 整体先于任何 initial 执行（elaboration 阶段，多个块按文本序）。
- construct 的特殊位只有两个：允许 connect、先于 initial。

### 8. 模块层次（class 内 construct + 动态 new）
- class 体内允许 `construct`（`new` 时执行，其 let 成为实例字段：子模块、通道对象）与 `initial`（`new` 时 spawn）。
- `this` 端口可作 connect 端点实现穿透：`connect(this.x, f1.x)`（外→内）、`connect(f1.y, this.y)`（内→外）。
- **output 驱动唯一性**：模块体代码 XOR construct 线，二选一，冲突编译错；input 穿透线计为其唯一连接。
- 动态实例化 v1 放行（仿真中途 `new`，initial 随之启动）——模块即 actor，静态接线是糖。
- Verilog generate 式参数化拓扑（按构造参数接不同线）= v2（会把多驱动检查推到运行期）。

### 9. 反压微观语义
- `write(v)`：反压通道满则挂起（Go channel 语义），空位出现后在下一 delta 提交 → 端到端流控自动反向传播，模块体一行流控代码不用写。
- `try_write(v) -> bool`：显式非阻塞逃生门。
- `y = x` 对 output 解糖为 `write(x)` → output 赋值是潜在挂起点，文档需与 wait 并列标注。

### 10. 终止与病态拓扑
- **终止 = 所有 initial block 结束 + 调度器静止**（静止本身不退出——服务器等外部输入是合法状态）。
- `exit()` = 显式退出，Verilog `$finish` 风格；含 `while(1)` 模块循环的程序由作者显式 exit。
- delta 上限（同仿真时刻内 delta 数，默认 ~1000，可配）：零延迟振荡环 → 运行期错误并报出环上模块与端口路径；**收敛组合环合法**（无编译期禁环——change-free 但 transaction 语义下"收敛"指环上不再有新事务）。
- 阻塞进程报告：Ctrl-C / debug flag 按需打印（死锁=挂起，报告不能等退出）。

### 11. 实现路线（BabyPenguin 先行）
- **Phase 1 — BabyPenguin**：VM 加调度器（delta 队列、静止检测、elaboration 阶段、wait-anything 原语）；语义层加 input/output/connect/construct；删 event/emit/on 关键字。VM 已是协程式（`IEnumerable<RuntimeFrameResult>`），属增量。
- **Phase 2 — 定稿与哨兵**：Documentation 新章节；`Tests/*.md` 以 Apply-To: BabyPenguin 做绿灯，EmperorPenguin 红灯哨兵（自动转绿机制）。
- **Phase 3 — EmperorPenguin**：协程状态机 lowering 落地后照规格实现（其 LLVM emitter 目前无状态机，本特性的硬依赖）。

## 遗留决定（未拷问 / 推迟，待后续确认）

- **端口载荷类型**：建议 v1 仅值类型（沿"events must be value-typed"先例）；引用类型将来经所有权转移（move 语义过通道）。
- **enum 上的端口**（原始提案提到 class/enum）：未决，v2——enum + initial 例程 = FSM 进程的形态值得单独立题。
- 端口数组 / 总线（`input data: i32[8]`）、表达式源、force/deposit：推迟。
- 文法细节：`initial while(1) { ... }` 为 `initial { while(1) { ... } }` 的糖；class 体文法需加入 initial 与 construct（现状只允许 on/event，`PenguinLang.g4:184-190`）；顶层文法需加入 construct。
- 迁移成本：`07`/`08` 文档、Examples、既有测试中的 event/emit/on 用法需重写为 Event<T> + wait 循环。

## 实战检验：LSP 服务器伪代码推演（2026-08-21 第二轮）

用六模块简易 LSP（StdioStream / JsonInputParser / LspMain / LspCompilationUnit / DocumentRouter / JsonOutputParser）对定稿语义做桌面推演。**结论：全场景可表达、反压全链路零代码成立、终止规则（静止≠退出）与 epoll 事件循环天然自洽；但暴露 6 个缺口/修订。**

### 推演中确立的模式
- **静态/动态拓扑二分**：会话级骨架（stdio↔parser↔main/router）用端口 + construct 接线；动态部分（每文档 unit）用通道引用进构造器、class 内 construct 里 `connect(通道, 子模块端口)`。
- **"源端 demux + enum 流"取代 select**：parser 按 method 把命令写进各消费组端口（载荷 = 该组的 enum），每消费组一条流，无需 `wait any`。
- **N:1 汇聚 = 多写者 Fifo**（多模块写同一 results 队列天然合法）；1:N 分发 = router。
- **wire 在全事务场景归零**：LSP 里连 stdio 行流/帧流都必须 FIFO（wire latest-wins 会丢行/丢帧 = 协议损坏）。wire 是"最新值即真相"的电平信号工具，不是消息工具——文档定位要写清。

### 暴露的缺口（按严重度）
1. **载荷引用类型**：JsonValue/Map/SemanticModel 立刻需要 → "v1 仅值类型"被实战否决大半。修订建议：string 及不可变引用放行；可变引用经 write/emit **所有权转移**（move）。
2. **通道关闭/EOF 未设计**：stdin EOF（客户端断开）应触发优雅退出。需要 `close(ch)` + wait 已关闭通道的确定行为（唤醒返回 none / 循环终止）。**未拷问过，需补一题。**
3. **output 驱动唯一性规则不完整**：模块内两个 initial 例程写同一 output（JsonOutputParser 的响应流+诊断流）按现规则未被禁止。推广为"恰一个驱动例程或一根线"；正确模式 = Merge 通道汇聚（再次验证 MQ 通道可由用户实现）。
4. **construct 带参数** 未明确：应作为 new() 构造器参数糖；建议参数自动成为实例字段（消除 `let x = x` 仪式）。
5. **select 观察项**：本场景被 demux 模式绕开；若模块天然需监听多个独立源且不宜在源头合流，`wait any` 会回归——暂不加，保持词汇表最小，记为观察项。
6. **外部事件源接入点**：runtime 需在静止时阻塞于 epoll_wait，fd 事件作为外部 delta 注入调度器（Q11"静止≠退出"的红利：事件循环与终止规则自洽）；io.stdin 应为 runtime 喂养的 Event 源，pipe 满 = POLLOUT 挂起 = 反压出口。

### 调度器推演（一条 initialize 请求）
epoll 唤醒 →(d0) stdio 读例程 write 行 →(d1) parser 醒、组帧解析、write session_q →(d2) LspMain 醒、write results_q →(d3) outp 醒、write frame_q →(d4) stdio 写例程 write(1)。约 5 delta/请求，每 delta 仅唤醒受影响模块，无轮询；静止即 epoll_wait。反压链：客户端不读 → pipe 满 → 写例程挂 POLLOUT → frame_q 满 → outp 挂 → results_q 满 → 全部 unit 挂 → doc_q 满 → router 挂 → parser 挂 → line_q 满 → stdio 读挂 → 内核缓冲满 → 客户端被节流。**零行流控代码。** 诊断走 LatestChannel（只留最新）合并风暴。

### 追问三轮（construct 参数 / 抛弃实例 / 策略封装）

1. **construct 参数 = 模块版构造器**（补缺口 4）：参数即构造参数、自动成为实例字段（无 `let x = x` 仪式）、construct 体 = 每实例 elaboration（唯一合法 connect 处），执行完毕后本实例 initial 例程才 spawn。与 `fun new` 并存时顺序：new（数据初始化）→ construct（组装接线）→ initial 启动。顶层 construct 无参数 = 文件级 elaboration。
2. **调度器 GC 根规则**（新）：有存活 initial 例程的实例是调度器 GC 根，全部 initial 结束后方可回收——丢弃实例引用不得导致模块蒸发。
3. **策略封装：模块只见 ISource/ISink，不见具体通道类型**（修正伪代码漏洞）：动态依赖的构造参数类型为 `mut ISource<T>`（消费，wait）/ `mut ISink<T>`（生产，write/try_write），具体策略（Fifo 策略/LatestChannel/wire）由创建者决定。接口方法集：ISource：wait 原语 + `current() -> T`（槽位裸读）；ISink：`write(v)`（按策略执行，反压可挂起）+ `try_write(v) -> bool`。
4. **端口语法的本质（统一表述）**：`input x: T` ≈ 命名 ISource<T> 视图 + 槽位裸读 + 权限矩阵；`output y: T` ≈ 命名 ISink<T> 视图 + `y=v` 解糖 write(v)。一切皆 ISource/ISink 组合，端口/connect/construct 是静态糖。
5. **实例化端口绑定（推荐采纳的糖）**：`new Foo(args, .port(view))` 把端口绑到源/汇视图，是 connect 的动态形态；多驱动检查在动态绑定为绑定期运行时检查（fail-fast）。动态模块因此可声明端口（接口文档 + 权限矩阵生效）。

### 追问四轮（construct 无参 / 运行期 connect / MultiInput）——取代三轮第 1、5 条

1. **正交三原则**：`fun new(...)` = 数据参数（uri、容量策略、模块引用、ISource/ISink 视图）；`input`/`output` = 流接口；**construct 无参数** = 每实例组装与接线块（唯一静态 connect 处，执行完 initial 才 spawn）。construct-params 方案与 `.port(view)` 命名绑定糖均作废——connect 一个词汇走天下。
2. **运行期 connect 合法**：静态可见连接（construct 内）编译期查多驱动；运行期连接在连接瞬间查（fail-fast）。未绑定 input 上 wait = 阻塞不崩溃（blocked report 可见）；Q3 未连接编译错仅对静态可见拓扑生效。
3. **MultiInput\<T\>**：≡ 模块私有合流队列 + 可 N 次 connect 的输入端口；每次 connect 注册源，事务按到达序合流，wait 如普通 input；per-source 策略（优先级/独立处理）等 wait-any 落地再开放。与"wirer 持共享 Fifo"并存：前者策略在模块内、扇入契约自文档化，后者策略在接线端。**引用传递不可消灭**（router 仍需经 fun new 拿到下游模块/端口视图）。
4. **扇出 + 反压规则（新）**：一个 output 连 N 个 sink 时，写事务须被全部 sink 接受才算完成（阻塞在最慢者），保证各 sink 事务序一致。
5. **模块退役（新）**：协议驱动（Close 命令 → break → initial 结束 → 实例可回收，配合调度器 GC 根规则）；死源在合流队列上静默；v1 无 disconnect。
6. **多驱动禁令只约束端口**：模块内部多写者通道（私有 Fifo 合流）合法且是"多循环写同一 output"的解法——每输入流一个消费循环写内部通道，单一驱动循环读写 output。

### 追问五轮（close=异常 / 引用载荷 / construct 语句化 STW / MultiInput=源集合）

1. **异常子系统成为前置依赖**（文法现状：无 try/catch/throw）：close 唤醒的 waiter 抛 `ChannelClosed`；construct 退出报 `WiringError`。v1 最小面：try/catch + 这两个类型 + 跨 wait 传播；无 finally/过滤/general throw。LLVM 侧 landing pad 成本 Phase 3 前评估。**close 级联（已定稿：完整级联）**：`close(ch)` → 所有 pending/future waiter 抛 ChannelClosed；对已 close 通道 write 也抛 → close(顶层通道) 一次拆整棵模块树（监督式关停，替代手写 Close 广播）。未捕获：模块 initial 内 → 该模块死亡 + 诊断进阻塞报告（程序继续）；顶层 initial 内 → 程序错误退出。
2. **引用载荷 = 传指针，风险接受**：v1 单线程协作调度下内存安全（无抢占窗口）；约定 payload 在 write 时冻结；风险兑现点 = 07 文档的多线程 job 派发——届时需 ISynchronized 载荷标记或 move 检查，记为多线程化前置项。
3. **construct 语句化 + STW**（统一模型）：construct = 事务性接线块（文件级/类内/方法体内皆可）；进入时调度器静止；体内禁 wait/阻塞写（自锁，编译期禁止）；退出时提交接线 + 延迟 spawn 块内 new 的模块 initial + 抛 WiringError；运行期 new 模块类仅 construct 内合法；禁止嵌套；增量簿记使退出检查 O(本块连接数)。成本 = 每次 STW 一次 quiesce 等待（协作调度下通常瞬时；长编译循环会延迟 STW，同 GC，需测量）；epoll 事件暂停期间内核缓冲不丢。
4. **MultiInput 修订为动态源集合端口**（非内部队列）：`wait mi` = epoll 糖（任一源先到先返回）；`for (src : mi)` 迭代源视图实现自定义策略；为此 `ISource<T>` 新增 **`try_poll() -> Option<T>`**（非阻塞试探）——自定义策略的正确写法是"try_poll 轮询一圈 + 全空再 wait epoll"，直接 for+wait 单源会卡死。

### 依赖的 stdlib 新通道类
`Fifo<T>(cap, policy)`（Backpressure/Drop）、`LatestChannel<T>`（只保留最新，诊断/状态用）、`MergeChannel<T>([sources])`（多源汇聚）。全部是 ISource+ISink 普通类（Q7 的红利）。

## 实战检验二：PL011 UART RTL 仿真（2026-08-21 追加）

UartTx/UartRx 子模块 + Pl011 顶层（寄存器进程 + construct 接线）+ 回环 testbench。**与 RTL 仿真器概念一一对应**：elaboration=construct、event wheel=调度器、delta cycle=delta、always 块=initial while(1)、$finish=exit()、信号=wire 端口、testbench=顶层 initial。

### 验证
- **wire 的主场**：uart_tx/uart_rx/status/baud_reg 全为电平信号（LSP 里 wire 归零、UART 里 wire 主角——两个实验合证 wire/FIFO 各有领地）。
- 裸读（电平采样）与 wait（事务）同模块混用自然；RX 自定时采样不被 TX 写节奏锁定。
- 无 waiter 的 wire 写 = 纯槽位更新，零开销（RTL 无敏感列表信号）。
- 变量源扇出（baud_reg → 两子模块）、回环一行、双向穿透、TXFF 轮询 + fifo 反压双保险流控全部成立。
- `wait <运行期 i64> tick`（波特运行时可配）必须支持——入档确认。

### 暴露的修订
1. **output 默认初值对称扩展（Q3 修订）**：`output line : bool = true;`——uart 空闲高电平，bool 零值 false 不够用。
2. **条件 wait 在端口上（推荐列入 Phase 1）**：`wait line == false` / `wait irq == true` = Verilog 电平敏感 wait()；08 文档 wait cond 对变量已有，扩展到含端口读的条件（端口读入敏感列表）。v1 轮询裸读可替代，但 realtime 模型下空闲轮询烧 wall CPU。
3. 边沿检测惯用法 `let v = x; while (x == v) { wait x; }` 记为 `wait change(x)` 糖候选；MMIO 读 = 请求/响应通道对 + id 配对（无新机制，stdlib 任务）。

## 实现勘误与偏差（2026-08-23 第六期 — 挑战套件 7 红哨兵全部转绿后定稿）

### A. 交付模型精化（Q4 与追问四轮-4 的调和，已实现）
扇出广播（每订阅者独立游标、事务按序全量交付）与 wire 合并（两次写之间下游未醒只见终值）在调度器轮次模型下统一为：
- **每次 write = 一个事务**；跨调度轮（delta）的写彼此独立，按序交付给**每个**消费游标（订阅线各自独立；hub 的直接读游标亦按序），晚到的消费者不丢事务（FanoutIndependentCursors 绿锁）。
- **同一轮内的多次写合并为终值**（pending 槽位替换；"下游未醒" = 同轮）——WireCollapseSameValue 绿锁。
- wire 与 FIFO 的分界由「写粒度 vs 轮粒度」给出：FIFO 每次 write 一事务（防丢），wire 每轮一个结算值（电平）。
- 订阅线共享单游标（竞争消费）仍适用于独立 LatestChannel 通道对象；广播扇出走 connect 的 subscribe 线。

### B. 端口初值种子（Q3 精化，已实现）
- **显式声明默认**（`output line : bool = true`）= 弱交付种子：time-0 首次 wait 唤醒一次（OutputDefault 语义），被任何真实写取代。
- **类型零值**（无声明默认）= 仅 current() 可见的种子：裸读确定性地得零值（PortBareReadDefaultZero），`wait port` 仍挂起等真实写（ComposedModule 语义）。
- 变量网线 hub 的连接时刻值 = 可交付种子（Q1 示例 + Q3 time-0 唤醒）。
- 引用类型载荷无可合成零值：保持种子空、裸读报清晰运行时错误（v1）。

### C. 裸读 = 结算点（Q1 落地，已实现）
- 裸读端口编译为：停车循环（每轮检查 `_sim_settled` = 上一轮无事务活动且本轮尚无活动）→ 结算后读 current() 槽位。Q1 典型例子 `x = 2; println(f2.y)` 打印 2。
- 结算观察计数器保证停车帧指纹逐轮变化，静止检测不会在读到结算值前终止程序；系统安静后计数器停止递增，指纹恢复稳定，静止退出不受影响。
- **construct 体内禁裸读端口**（结算点即挂起点，construct 禁等待）= 编译错。
- `wait change(port)` 边沿检测按原样采样**原始电平**（每轮即时槽读，不结算）——结算粒度会错过单轮脉冲；条件 wait（`wait port == v`）内的端口读保持结算语义。

### D. 静态拓扑检查（Q2/Q3/Q9 落地，已实现，错误码 E_WIRING）
- 多驱动 input：同一实例的 input 被两根 connect 线驱动 = 编译错（跨 construct 块按作用域池合并检查）。
- 未连接 input：construct 内 `let x = new C()` 的每个无默认 input 必须在同池内被连接，否则编译错；动态实例化（construct 外 new）不做静态检查，保持运行期优雅停车（UnconnectedInputWaits）。
- output 驱动唯一：体写（赋值解糖 + write()/try_write() 调用）XOR construct 穿透线，冲突编译错；两个不同例程写同一 output 同样编译错（合并流走通道）。
- 检查按作用域池运行：顶层 construct 共享命名空间池，类 construct 共享类池；静态可见性之外的拓扑（别名、嵌套成员实例化）留待运行期检查。

### E. 偏差 1 —— 终止规则（v1 保留实现行为，文档化分歧）
设计 Q10/Q11 = 「静止≠结束」。**BabyPenguin v1 实现为：静止即正常退出 0**（两轮无信号 + 挂起作业指纹不变）。理由：v1 无外部事件源，「等待外部输入」与「死锁」不可区分，保持等待意味着挂起进程；绿锁 QuiescentBlockedWaiter 锁定该行为。epoll 集成（外部 delta 重置活动计数）落地时切换到设计语义——外部事件源到位后「静止≠退出」才可判定。

### F. 偏差 2 —— connect 汇侧通道（v1 限制，文档化分歧）
设计 Q6：connect 汇 ∈ {input 端口、通道}。**BabyPenguin v1 汇 ∈ {input 端口、MultiInput}**——通道对象作汇（LSP 式 `connect(port, fifo)` 两段接线）推迟：v1 内用 MergeChannel 构造组合（源侧）或 MultiInput（扇入汇）表达，`connect(q, f2.x)` 的通道作**源**侧已支持。通道汇侧列入 Phase 2。

### G. 诊断格式（ PanicInModuleInitial 修复）
未捕获的程序级运行时错误（panic / 通道错误）报 `Uncaught runtime error: <msg> (code <n>)`（stderr，退出非零），先冲刷缓冲的程序输出；企鹅层数字错误码不再与编译器 ErrorCode 枚举值混用。

## 定稿示例

基础（原文例按定稿语义修正）：

```penguin
class Foo {
  input x: i32;
  output y: i32;
  initial while (1) {
    let v : i32 = wait x;   // 原 poll(x)
    y = v;                  // 解糖 write(v)
  }
}

construct {
  let x : mut i32 = 1;
  let f1 = new Foo();
  let f2 = new Foo();
  connect(x, f1.x);
  connect(f1.y, f2.x);
}

initial {
  x = 2;
  println(cast<string>(f2.y));  // 2 —— 端口读 = settle point
  exit(0);                      // while(1) 模块在场，显式结束
}
```

插 FIFO（防丢 + 反压）：

```penguin
construct {
  let x : mut i32 = 1;
  let f1 = new Foo();
  let f2 = new Foo();
  let q : mut Fifo<i32> = new Fifo<i32>(8, FifoPolicy.Backpressure);
  connect(x, f1.x);
  connect(f1.y, q);
  connect(q, f2.x);
}
```

组合层次（穿透）：

```penguin
class Bar {
  input x: i32;
  output y: i32;
  construct {
    let inner = new Foo();
    connect(this.x, inner.x);   // 外→内穿透
    connect(inner.y, this.y);   // 内→外穿透（this.y 唯一驱动 = 这根线）
  }
}
```

事件（原 event/emit/on 用法迁移）：

```penguin
let clk : mut Event<i32> = new Event<i32>();   // first-class，可传引用，免钻孔

fun deep(c : mut Event<i32>) { c.emit(1); }     // 自由函数也能发

initial {
  while (true) {
    let v : i32 = wait clk;
    println(cast<string>(v));
  }
}
```
