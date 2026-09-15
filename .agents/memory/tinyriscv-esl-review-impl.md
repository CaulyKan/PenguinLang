# tinyriscv ESL 三点 review 实施（2026-09-16，分支 feature/tinyriscv-esl-ports）

对 2026-09-16 review（/tmp/states.txt 末节）三点意见的落地：点3 → 点2 → 点1，
每点独立提交独立回归。提交链：3103cf41（点3）→ 335265b9（点2）→ 点1（见 git log）。

## 点3 — ESL 去 pointer intrinsic
Rom/Ram `#template(WORDS)` 值参改构造参数 + `std.Vector<u32>`（**Vector.resize_to
不零初始化**——ctor 里 push 循环零填充保确定性）；RegFile 同；Bus/Clock 弃用
scheduler 内部 `_ChanList`。**esl 库自此依赖 vector.penguin（pass3-only，
BP 编译不了 Clock.penguin）**——消费方 Compile.Args/项目 sources 必须带上。

## 点2 — --meta-src 显式列表
CompilerConfig/Project(main-sources 键)/main/MetaEngine（删 ~80 行启发式）。
meta 列表 = 原标记三件套（Asm/Firmware/Bit）——InsnTable/MemMap 的 #fun 从不
依赖自身文件进 unit B（走宿主 trampoline），别往列表里加（#fun 定义与引擎合成
副本会重定义）。--meta-src 文件独立读取，不必同时是编译源。旧 flag 传了报
exit(2) 指路错误。

## 点1 — 端口化布线（48614 周期锚点保持）
架构决策（全部以周期等价性推导）：
- **if_id/id_ex = esl.Pipe<T>**（新原语：两相提交深度1通道，write→in 侧，
  Clock commit 相公布，无公布=气泡≡NOP 冲刷——Hold_If/Hold_Id 不需要显式
  flush）。实现 ISource+ISink+IChannel+IFuture+IReg 即可作 connect 源。
- **写回 = output port 线**（LatestChannel 同拍 delta 投递 + RegFile 边沿提交
  ≡ RTL posedge 写）。**不能**用 Pipe：发布晚一拍（48615）。
- **redirect/jump 决策 = 纯函数读 id_ex pipe 的 current()**（committed 视图，
  不被消费破坏）。**不能**用 Pipe<Redirect>：决策当拍生效（周期 T 用），
  Pipe 化晚一拍（48616=3 周期代价，RTL 是 2）。
- Ctrl 存 pipe+regs 引用调 published()；Execute 的 IBusMaster.bus_req 走端口
  字段 current()（IChannel 上的 try_poll/current/write/try_write 编译器自动
  重写为接口 cast）。
- main.penguin 全部进顶层 construct{}（hoist 成全局 + __construct_0 在
  elaboration 相、任何 initial 前同步执行——ROM 装载放辅助函数
  load_firmware，别让 meta 调用当全局初始化器）。模块 initial 在 ctor 里
  co_spawn 排队，sched_run 才跑——connect 全部就位后才有人 wait，无竞争。

## 本轮两个真编译器 bug/坑
1. **pass-8 晚期特化的接口默认方法 vtable 悬空**（已修 SemanticMonomorphize
   `finish_late_spec_def`）：connect 生成代码按需创建的 LatestChannel<W>（无任何
   显式 LatestChannel 注解时）其 IFuture vtable 的 do_wait 槽指向未发射的模板
   方法 `__builtin_IFuture_do_wait` → 链接错。根因：pass-3 定点会从每个特化类
   收集 `impl IFoo<u8>` 接口实例化（run() 896-905），晚期路径漏了这步。修复 =
   catch-up 前先 `late_ensure_def_interface_impls`（红→绿测试
   Tests/PortTest/LateWireDefaultMethodVtable.md，BP+pass3 双绿）。
   注意 PortPipeline 一直是绿的只因 `let top : mut LatestChannel<i64>` 显式
   注解走了 pass-3 全协议。
2. **connect 线使 Event 广播对端口消费者变为非丢失**：模块 initial 在
   elaboration 排队、Clock 的 initial 先跑，首个 emit 被**线缓冲**（LatestChannel
   线永不丢事务）→ 首次求值提前到周期 1 → 全仿真少一拍（48613）。旧模型是
   模块直 park 在 Event 上（丢失=复位沿）。修复 = esl.Clock 显式跳过第一次
   emit（`if (this.cycles > 0)`）——复位沿从"广播碰巧丢"变显式语义，对直连
   evt 的 smoke 也等价。

## 杂项
- 端口名不能用关键字 `in`（解析错）——用 `din`。
- Pipe 期望值计算别忘复位沿：evaluate 从周期 2 开始（周期1无 emit）。
- `wait <payload-event>;` 语句式（丢弃值）合法——smoke 的 `wait this.clk.evt;`
  在 Event<i64> 下不用改。
- 值类型包：class + `impl ICopy<Self>;` + 无默认值字段 + `new P()` 后字段赋值
  （CopyTest 模式）；经端口/通道传值拷贝，无别名。
