# tinyriscv ESL 建模（2026-09-15，分支 feature/tinyriscv）

计划：`.agents/plans/tinyriscv-esl-model.md`。M0-M7 全部完成：pass3 原生跑
`Examples/tinyriscv`，UART 打印 hello world，**48614 周期**（×20ns@50MHz），字节稳定。
提交链：04dc68e0(M0+M1) → 1a7b1bd2(M2) → 4b3363d2(M3) → 00983f41(M4) → 5bdbce67(M5)。

## 交付结构

- `EmperorPenguin/others/libpenguin-esl/`：Clock（两相 evaluate/commit，1 tick=1 周期）、
  Reg<T>（load/flush-default/freeze 三模式，构造时向 Clock 注册）、Bit、Mem（_malloc+`#__load/#__store`，
  总线地址按容量掩码）、Bus（nibble 译码 + m0>m1 固定优先级 + IBusDecoder 元生成译码路径）。
  smoke/smoke.penguin 字节精确自测。
- `Examples/tinyriscv/`：Pipeline(共享流水寄存器)+Ctrl(纯函数决策)+Fetch/Decode/Execute 三进程、
  RegFile、Uart、sw/Asm(两遍汇编器+reference_asm.py 参考编码器)、InsnTable/MemMap(#class 表→#fun 生成)。
- 测试：TinyRiscvAsmGolden（对照 python 参考逐字）、TinyRiscvHelloWorld（端到端+周期数锁定）。

## 关键设计事实（防再踩）

- **顺序无关三要素**：evaluate 只读 committed（Reg.out()）、仲裁输入只来自已提交状态、
  ctrl 决策（jump/hold）是 id_ex 已提交字段的纯函数——三进程任意唤醒序等价。
- **EX 级读寄存器堆**（id_ex 带索引）≡ RTL 的 ID 读 + regs.v 写旁路（raddr==waddr 转发 wdata），
  值与周期等价且免进程顺序依赖。RTL 核对依据：/tmp/tinyriscv 的 ctrl.v/rib.v/regs.v（优先级
  m3>m0>m2>m1；m0 占用时 m1_data_o=INST_NOP=0x1；hold≥If/Id 冲刷 NOP、仅 pc 冻结）。
- **load/store 气泡**：EX 访存当拍 pc 冻结+取指得 NOP——气泡插在下一条指令之后（1 周期）；
  **taken 分支**：冲两级（2 周期）。

## 编译器修复（全部有回归用例，BabyPenguin+Pass3 双绿）

1. **hex/0b/八进制字面量**：解析期规范化为十进制（两前端：PenguinLangParser
   NormalizeIntegerLiteral / EP normalize_int_literal）。原状：BabyPenguin E_RESOLVE_TYPE；
   EP 把 0xFE 的 'e' 误判浮点形状、0x.. 直发 LLVM IR（那里 0x=浮点语法）、017 静默按十进制。
2. **cast<string>(char) 段错误**：emitter 补 char 臂 + `_emperor_char_to_string`（UTF-8）。
   注意：字符字面量 'B' 绑定为 i64 码点（既有语义），cast<string>('B')="66"，需 cast<char> 中转。
3. **调度器 idle 跳变饿死同刻 delta 轮**（双编译器）：EP 裸 wait 在跳变后被唤醒、BP 唤醒丢失。
   修复＝跳变门加 activity（两调度器；wait 自身 ready 解析即活动，足以钉住同刻轮）。
   ⚠️ 试过把裸 `wait;` 降成 0 截止定时器——**已撤销**：Fifo 背压写者的条件驻留每轮重臂 0 定时器
   → 每轮 activity → 时间永不前进 → FifoCap1BackpressureOrder/ExitDuringParkedWriter 活锁。
4. **--meta-with-user-sources**（新 opt-in 引擎能力）：unit B 纳入 `// meta: unit-b` 标记
   （且剥注释后无协程关键词）的用户文件；#fun 按声明命名空间+文件 using 包装＋顶层转发器
   （JIT 裸名查找）；_template_*/__specializing_* 与 flag-off 保持顶层合成（动了会断 bootstrap：
   std 里的 #fun 被顶层 trampoline 裸名调用）。unit B 继承接协程 flag+scheduler stdlib、
   跳过定义位 #fun 调用拼接（防元重入）。

## 坑

- `let` 后重赋值必须 mut；`List.at()` 取 u64；对 cast 临时值不能直接成员赋值（先绑局部）。
- `.penguins` 的 `*.penguin` 通配会把 smoke 卷进模型——库自测放子目录。
- 测试 runner 会把测试代码 source.penguin 与 Args 里的项目文件**一起**编译——项目型测试的
  Test Code 只能放注释。
- 测试期望块内含空行/尾随空行时逐字节核对（println("") 与迟到字符顺序）。
- 改 src/meta/* 或 src/bound/* 都要全量 `env -u MAKE make bootstrap`（~8min）；scheduler.penguin
  是磁盘读取的 stdlib，改它不用重建。
