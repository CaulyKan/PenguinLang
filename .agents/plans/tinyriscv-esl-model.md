# tinyriscv × PenguinLang 建模计划

> 状态：已批准（2026-09-15）
> 成功标准：原生 pass3 编译运行 `Examples/tinyriscv`，执行 RV32I 固件，经 UART 模型打印出 `hello world`。

## 已确认的范围决策

- **最小 RV32I 子集**：只实现 hello world 所需 ~9 条指令（LUI/ADDI/ANDI/LBU/LW/SW/BEQ/BNE/JAL），无 CSR/中断/Timer/GPIO/SPI；模块划分仍对应 tinyriscv（pc_reg/if_id/id/id_ex/ex/regs/ctrl + rom/ram/uart/rib）。
- **深度 meta（仅 native）**：译码表、内存映射、固件汇编全部用 `#class`/`#fun`/反射编译期生成；BabyPenguin VM 不可编译本示例。
- **仅原生 pass3 验证**：先 `make bootstrap` 一次，之后全部用 `build/bootstrap/pass3` + `EmperorPenguin/emperor` 迭代与验证。
- **timing 原则**：**1 tick = 1 个时钟周期**（参照 tinyriscv testbench 50 MHz → 1 tick ≙ 20ns，文档注明换算关系；仿真时间不追求严格正确，但周期数要有 RTL 参考性——分支代价、访存气泡、UART 波特率周期数均与 tinyriscv 一致）。**不做"每指令 1 tick"**——仿真时间由全局时钟推进，与指令无关。

## 探索确认的关键事实（设计依据）

- ports/`connect`/`construct`、`async`/`wait`/`wait n tick`、`Event<T>`、`Fifo`/`LatestChannel`、tick 时钟在 EmperorPenguin 均已实现（`--enable-coroutine`，`emperor` 脚本透传；AGENTS.md 相关"未实现"描述已过时，将作为第 1 部分文档纠错）。
- 语言无定长数组；native 用 `std.Array<T,N>`（`std/penguin/array.penguin`，额外源引入）。
- `#fun` JIT/`#class`/反射仅 pass2/3；meta 引擎 "Phase 6 v2 in progress"，缺陷即第 1 部分工作。
- tinyriscv 事实（当前 master）：
  - 3 级流水（pc_reg → if_id → id 组合 → id_ex → ex 组合）；
  - hold 编码 `Hold_None/Pc/If/Id`（`>=` 比较；pipe 寄存器是**冲刷为 NOP(0x00000001) 而非冻结**，仅 pc_reg 冻结）；
  - RIB 总线组合逻辑单周期、m0(EX 访存) > m1(取指) 固定优先级、`addr[31:28]` 译码；
  - 内存映射：ROM@0x0 / RAM@0x10000000 / TIMER@0x20000000 / UART@0x30000000 / GPIO@0x40000000；
  - UART 寄存器：CTRL=0x00（bit0 tx_en）、STATUS=0x04（bit0 tx_busy）、BAUD=0x08（默认 440）、**TXDATA=0x0C（写即发，需 CTRL[0]=1 且 STATUS[0]=0，否则丢弃）**；
  - 复位 PC=0；x26=1 为仿真结束约定（testbench 直接读 regfile）；testbench 50 MHz。
- 关键调度语义：tick 仅在所有例程挂起时前进。时钟驱动模型下每级每周期挂起在 `wait clk` 上 → 时间自然推进，忙轮询固件每轮询一次消耗真实周期，无死锁、无需任何 per-instruction 计时。

## 时钟驱动仿真核心（本计划的关键设计）

两相时钟（evaluate / commit），即事件驱动仿真器的标准做法，保证多进程唤醒顺序无关的正确性：

```
# 时钟进程（libpenguin-esl/Clock.penguin）
initial {                       # 每周期：
    clk.emit(void);             #  相位1：所有挂在 clk 上的模块进程被唤醒，
    wait 0 tick;                #        做本周期组合计算 + 写 Reg 的 in 端（settle）
    commit_all();               #  相位2：原子提交所有时钟寄存器 in→out
    wait 1 tick;                #  时间 +1 周期
}
```

- `Reg<T>`（libpenguin-esl/Reg.penguin）：两相时钟寄存器——`write(v)` 只写 in 端，`out` 在 commit 时更新，读永远读 out（镜像 Verilog 非阻塞赋语义，消灭读写顺序竞态）；支持 tinyriscv `gen_pipe_dff` 的三种提交模式：**载入 in（正常）/ 冲刷为默认值（if_id、id_ex 被 Hold_If/Hold_Id 冲成 NOP 气泡）/ 冻结保持（pc_reg 被 Hold_Pc 冻结）**。
- wire/电平语义由语言原生 `LatestChannel` 覆盖；`Reg`/`Clock` 是 ESL 库补齐的最基础 RTL 原语。

## 交付物三部分

### 1) EmperorPenguin 本体修复/改进（按开发中实际遇到的问题驱动）

- meta 引擎缺陷修复（重点预期：`#fun` 字符串实参/返回与 splice，编译期汇编需要）。
- 协程/调度器缺陷修复（本示例是这些新机制的首个大型压测）。
- 沉淀的可复用资产并入 `EmperorPenguin/std/penguin/`。
- 文档纠错（AGENTS.md/README 过时的协程描述）。
- **每个可复现 bug 立即落 `Tests/<Category>/<Name>.md` 回归用例**（AGENTS.md 强制要求；未修复的写红色哨兵用例并注明根因与"修复后应转绿"）。

### 2) 芯片建模公共设施 → `EmperorPenguin/others/libpenguin-esl/`（新建目录）

- `Clock.penguin` —— 两相时钟（posedge `Event<void>` + commit 相 + tick 推进 + 周期计数 `_sim_now()` 包装）。
- `Reg.penguin` —— `Reg<T>` 两相时钟寄存器（载入/冲刷默认值/冻结三模式）、`RegRst<T>`（复位值）。
- `Bit.penguin` —— 位切片/符号扩展/hex 解析纯函数。
- `Mem.penguin` —— 基于 `std.Array<u32,N>` 的 ROM/RAM：byte/half/word 读写（SB/SH 读改写）、`load_image`。
- `Bus.penguin` —— `IBusSlave` 接口 + `addr[31:28]` 译码互连 + **m0>m1 固定优先级组合仲裁**（`request(master, addr, we, wdata) -> u32`，评估相位内函数调用，忠实 RIB 的单周期组合总线语义；m0 占用时 m1 取指得到 NOP 气泡）；Fifo/队列只用在 RTL 中真实存在缓冲的地方（UART 串行化等），不做会扭曲周期计时的总线队列化。

### 3) tinyriscv 模型 → `Examples/tinyriscv/`

```
Examples/tinyriscv/
  README.md               # 模块映射表（rtl ↔ 模型）、1 tick ≙ 1 clk cycle(20ns@50MHz) 说明、简化点清单、运行方法
  tinyriscv.penguins      # sources=[main + core/* + perips/* + sw/* + ../../…libpenguin-esl/*.penguin + std/penguin/array.penguin]
                         # flags=["--enable-coroutine","--enable-meta"]
  main.penguin           # SoC 顶层：construct 布线、时钟实例、固件烤入、结束时打印周期数报告后 exit(0)
  core/
    Fetch.penguin        # pc_reg + 取指进程：Reg<u32> pc（冻结/跳转语义），经互连 m1 口取指，气泡=NOP
    Decoder.penguin      # 纯译码函数 u32→Decoded（meta 生成的译码表）；立即数提取
    Execute.penguin      # ex 组合逻辑函数 + 写回进程：ALU/访存（互连 m0 口）/jump_flag→ctrl；regs 写在 commit 相
    RegFile.penguin      # x0..x31 单写口（x0 恒零）；EX→ID 旁路在模型中由 commit 语义自然覆盖
    Ctrl.penguin         # hold 聚合（EX jump/div→Hold_Id 冲刷两级；RIB m0 占用→Hold_Pc 仅冻 PC），忠实优先级
  perips/
    Uart.penguin         # CTRL/STATUS/BAUD/TXDATA 寄存器；TX 时序进程：10 bit × (baud+1) 周期/bit
                         # （默认 440+1 ≙ 115200@50MHz），发完 print 到宿主；STATUS 忙位可被固件轮询
  sw/
    Asm.penguin          # 迷你 RV32I 汇编器（普通 penguin 函数，两遍扫描：标号+编码）
    Firmware.penguin     # 汇编源码字符串 + #fun 编译期汇编烤入 ROM 常量
```

- 固件（忠实 tinyriscv uart_tx 例程：轮询 STATUS 后写 TXDATA）：

```
lui t0,0x30000; addi t1,x0,1; sw t1,0(t0)        # CTRL=tx_en
loop: lbu t2,str(t1); beqz→done
poll: lw t3,4(t0); andi t3,t3,1; bnez→poll        # 轮询 STATUS.busy——每次轮询消耗真实周期
sw t2,12(t0); addi t1,t1,1; j loop
done: li x26,1; j .                                # 仿真结束约定
```

- 深度 meta 三个落点：①`#class` 指令表 → `#fun` 生成译码 match；②`#class` 内存映射表 → 生成互连从设备译码；③`#fun` 编译期执行汇编器，固件字烤进常量。
- 结束报告：`x26=1` → 打印 `"hello world" 用时 N 周期 (≈ N×20ns @50MHz)` 类摘要后 `exit(0)`，体现周期参考性。

## 里程碑（每步有验证门，分支 `feature/tinyriscv`，逐里程碑提交）

- **M0 准备**：计划落 `.agents/plans`；建分支；后台 `make bootstrap`；精读 `Documentation/10_MetaProgramming.md`、`.claude/plans/meta_plan.md`、`Tests/MetaProgramming/`、`Tests/PortTest/`；写 penguin 代码前调用 `penguinang-coding` skill。
- **M1 ESL 核心**：Clock/Reg/Bit/Mem/Bus + 独立冒烟程序（pass3 运行；验证两相提交顺序无关性、冲刷/冻结模式、优先级仲裁、周期计数）。
- **M2 汇编器**：运行时汇编固件镜像；金样测试逐指令断言编码字（对照手算编码 byte-exact）。
- **M3 单进程周期精确 CPU 先行点亮**：一个进程每时钟步进全部三级（if_id/id_ex 用 Reg，时序已正确）→ **pass3 原生跑出 `hello world\n`**（成功标准最早达成点；UART 先简化为瞬时发送也可先跑通再补波特率时序）。
- **M4 拆分三进程 + 完整 hold/冲刷语义**：Fetch/Decode(组合)/Execute 三进程 + Ctrl hold 聚合 + 取指气泡；UART 波特率周期精确 + 固件忙轮询；结束后周期数报告。
- **M5 深度 meta 化**：三个 meta 落点逐个替换手写代码；缺 string-splice 等 meta 能力则先做第 1 部分引擎补齐。
- **M6 上游回灌**：汇总所有编译器修复 + 回归用例 + 文档纠错。
- **M7 收尾**：`Tests/ExampleTest/TinyRiscvHelloWorld.md`（Apply To: Pass2, Pass3；stdout 含 `hello world\n`）+ 汇编器/译码金样用例；README；全量 `make test` + `dotnet test` 无回归。

## 主要风险与对策

- meta JIT 能力不足（string 实参/返回）→ M3/M4 不依赖 meta 可先跑通；必要时作为第 1 部分实现该能力。
- 两相时钟的 commit 相实现有隐藏调度坑（如 `wait 0` settle 不彻底）→ M1 冒烟程序专门覆盖多模块乱序唤醒；有问题按调度器 bug 修复并落用例。
- std.Array 语义/性能问题 → 备选 std.Vector 或 utils.List。
- pass3 迭代编译速度 → 示例程序小，秒级；bootstrap 只跑一次。
