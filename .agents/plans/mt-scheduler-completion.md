# 新调度器完善计划（mt / free-mt 收尾与翻盘）

日期: 2026-10-12
基线: master `bbb9e61a`（654c21a0 合入第一版多线程调度器 + 后续两修复）
前序计划: `.agents/plans/mt-scheduler-implementation.md`（M0–M7 全部实质落地，进度项全部勾完）
设计文档: `docs/zh/wip/concurrent_scheduler.md`（唯一事实源）

## 执行状态（2026-10-12 当日，分支 feature/mt-scheduler-completion）

- **批次 1 完成**（d7e28639）：WaitChangePortMt/TinyRiscvMultiCoreMt 钉死、known-open 关闭、
  设计文档对账。
- **批次 2 完成**（e52b667c）：CI 网（EMPEROR_THREADS=4 子集 + shuffle 三种子）+ runner
  `--exclude` + 红灯注入验证。**本地预验证时抓到三个 N>1 内核竞态并当场修复**
  （c5080812：free-mt unsubscribe 无锁、pop→RUNNING 幻影窗口破坏轮屏障、co_all_head
  无锁摘链段错误 + gc_poll/live_count/lib_park 的 TSan 债）。
- **批次 3.1 完成**（aca10fbd）：定向唤醒——mt-N1 在 8 核首次全面跑赢 classic（4330 vs
  4938ms，−12%）；kicks −94%、switchins −85%。**批次 3.2（glock 去中心化）重新定界**：
  需重建幻影协议的临界区结构（1–2 周级），且 ESL 负载可偷宽度 4/17 限制 N>1 上限——
  留作独立后续（见 mt-scheduler-implementation.md 的 3.2 重新评估节）。
- 批次 4/5 未动（按需/决策批次）。

## 一、现状快照（2026-10-12 复核）

**已落地**（细节见前序计划进度节）：M0 语义统一（含 BabyPenguin SimScheduler 重写）、
M1 原子量、M2 lite 封闭分析（-Wconcurrency + ISync 标记重开）、M3 微内核逐线程化、
M4a/b 执行引擎下沉 + 多 worker、M5 v0 + v1 一级（每线程 arena + 无锁小对象快路径 +
-O2 默认 + 压力阀 + poll 升级 STW + 回边安全点）、M6 延迟构件族全齐 + Ownership v1 +
逃逸分析、M7 free-mt v1（AsyncQueue）。唤醒归档协议五连环洞已根治（24ceba1f）、
no-consume futures 协议落地（64a73086）、TSan N=1/2/4 零报告。

**今日复验证（关键更新——两条 known-open 已治愈，记录未关闭）**：
- `Tests/TimingTest/WaitChangePort.md` 的 pass3-mt 永久挂起：**已修复**。pass3 +
  `--enable-multithread-scheduler` 编译运行正确，输出 `edge1\nfalse`（与 BabyPenguin
  一致，种子先行）；N=4 ×5、N=8 ×3 全对。测试的 Apply To 仍只列 BabyPenguin。
- tinyriscv N8 丢 'r' + 4416 周期（64a73086 known-open 陈旧 STATUS 面）与 N≥5 差一拍：
  **12/12 锤击全对**（8 核 × EMPEROR_THREADS=8，输出唯一 md5、48614 周期恒定）。
  原 1/12 复现率的形态未再出现。
- 门禁：MtSchedulerTest 50/50、FreeMtTest 6/6（今日实跑）；最近全矩阵 1673/0/0/220。

**性能基线（2026-10-07，tinyriscv，交错 ×5 中位，ms）**：

| 核 | classic | mt-N1 | mt-N2 | mt-N4 |
|---|---|---|---|---|
| 1 | 816 | **566** | 1419 | 2069 |
| 2 | 1317 | **1017** | 2121 | 2925 |
| 4 | 2572 | **2372** | 4026 | 5229 |
| 8 | **4930** | 7036 | 8790 | 15105 |

结论：≤4 核 mt-N1 最优；**N>1 恒亏**；8 核 classic 领先 mt-N4 三倍。RSS 反向占优
（mt 42–48MB vs classic 151MB）。

## 二、缺口清单

### A. 欠账钉死（已治愈但无回归保护——最高性价比）
- A1 WaitChangePort-mt 形状无测试钉住；测试文件里残留过期 NOTE。
- A2 tinyriscv N8 known-open 记录未关闭（12/12 已过，需长锤定案）。
- A3 设计文档状态头过期：写着"未做 M5 v1 与 M7"，实际 free-mt v1、arena v1 均已落地；
  15 章里程碑表 M7 行未更新。

### B. 性能可扩展性（最大真实缺口：mt N>1 翻盘线）
- B1 **kick_futures 是 wake-all**（`scheduler.c:1941`，Mesa 语义：任何交付唤醒全部
  CO_COND 停靠者重轮询）。N≥5 下 17 个等待者 × 每拍 ~2.3 次兜底 kick，全员醒着使
  between_parked 失效。→ 定向化：等待集挂到结果容器/构件，按完成目标唤醒。
- B2 **mt_glock 单锁 + 每 delta 屏障**是结构性墙（8 线程 63% CPU 烧进系统调用）。
  per-thread runq 已存在但 push/pop/唤醒握手仍全走 glock。→ §12 "无锁双端队列后置"
  正是此意：runq 无锁快路径、握手分片、一次临界区批量 fan-out（mt_wake_locked 已
  提取）、定向 eventfd 替代全员信号。
- B3 **mt-N1 settle 超线性**（8 核 = 1 核 12.4×）：结构性——延迟语义下链深 D 需 D 拍、
  每拍一次边界往返（profile：run_delta 26.9% + kick/cancel 14.5%）。应用侧缓解
  （esl.Clock settle 深度参数化/管线适配）单列，不动内核。
- B4 free-mt 无虚时钟（`wait n>0` 运行期拒绝）——设计如此，非缺口；文档化即可。

### C. CI 防回归网（M6 验收项未满足）
- dotnet.yml **零 mt/free-mt 覆盖**（今日核实：无 EMPEROR_THREADS、无
  --enable-multithread-scheduler/--enable-free-mt、无 --shuffle-deltas）。全部 mt
  验证至今靠本地手工门禁。M6 验收"检测器在 CI 跑多轮种子"未兑现。

### D. 功能完成度（设计文档开放问题，按需排期）
- D1 挂起诊断完整版（开放问题 10 / §7.4）："还有 N 个协程分别等什么"——HANG_WATCHDOG
  是起点，缺按停靠类别（futures/owed/cond/fd/定时器）聚合的人读报告。排查价值高、量小。
- D2 libmeta effects 字段（开放问题 9）：库元数据携带副作用 → 跨库 spawn 放宽
  （现"跨动态库创建一律固定"）。
- D3 多写者 Signal 合并规则 + 多等待者按身份定序（开放问题 3 / §6.9）。
- D4 M5 v1 第二级：双域 GC（本地回收器 + 共享堆 + 移交点复制，§10.3–10.6）。
  批次 3 后 profile 再决定是否值得。
- D5 Ownership 精度：组迁移（开放问题 4）、深拷贝环检测开销（开放问题 6）、
  v1 剩余不新鲜形态（函数返回值/字段读）。
- D6 ICopyCross 决策（开放问题 11）——等 Ownership 实测数据。
- D7 M2 遗留核对：初始例程判定、封闭类全局内部可变 info 诊断（设计决议 7）。

### E. 决策点（用户裁决，不由本计划代决）
- E1 mt 何时提升为原生默认（开放问题 2 / 文档状态头"迁移决策"）。门槛建议：
  A/C 批次完成 + B 批次至少不劣化 + 13.2 审计在 mt 形态全过 + CI 网稳定运行。
- E2 文档定稿：性能报告（2026-10-07 基准 + 翻盘后复测）入文档 → 移入
  specifications/ + 英文镜像（双语 1:1 纪律，用 penguinlang-doc skill）。

## 三、执行批次

### 批次 1 — 欠账钉死 + 文档对账（~半天）
1. 新增 `Tests/TimingTest/WaitChangePortMt.md`（Apply To: EmperorPenguin Pass3；
   Args: `--enable-coroutine --enable-multithread-scheduler`；期望 `edge1\nfalse\n`），
   删 WaitChangePort.md 的过期 NOTE（"add Pass3-mt back once fixed"已兑现）。
2. tinyriscv N8 长锤 30 次（今日已 12/12）；全过则关闭 plan 文件的 known-open 记录，
   N≥5 差一拍按"已消失"处理；仍偶发则按原处置写入设计文档 §7.1 非承诺条款。
3. 设计文档对账：状态头（M7/arena 落地）、里程碑表 M7 行、§7.3 实现状态注补
   no-consume 协议与今日复验结论。
4. 门禁：全矩阵 + `make bootstrap` 收敛。
分支：`feature/mt-scheduler-completion`（或直接小提交，量小）。

### 批次 2 — CI 防回归网（~半天，先于批次 3）
dotnet.yml build job 增步骤（控制时长，全部子集 + 时间盒）：
- MtSchedulerTest + FreeMtTest × pass3（约 20s）；
- `--shuffle-deltas`（EMPEROR_SHUFFLE_DELTAS）3 种子跑确定性子集（MtDeferred* 家族）；
- tinyriscv 4 核 mt × EMPEROR_THREADS=4 × 2 次（单次 <10s，逐字节断言）。
验收：CI 红灯可发现 mt 回归（本地故意注入一次验证）。

### 批次 3 — 可扩展性翻盘（核心工程，~1–2 周）
目标：8 核 mt-N4 ≤ classic 4930ms；N=8 不再比 N=4 差一个量级。
按风险从小到大、每步独立验收：
1. **kick 定向化**（B1）：futures 等待集从全局链改为按结果容器/构件定向；无法定向的
   保留兜底 wake-all（正确性回退面）。
2. **glock 去中心化**（B2）：runq 无锁快路径（MPSC/双端队列）+ 握手分片 + 批量
   fan-out + 定向 eventfd。这是唤醒归档协议敏感区（§12.1 教训 1 幻影队列项、
   24ceba1f 五连环洞）——小步提交，QLOG/HANG_WATCHDOG/LOST_SCAN 装备全程保留。
3. 每 step 门禁：全矩阵 + TSan N=1/2/4 + tinyriscv N8 ×12 + 全设置基准（交错 ×5，
   与 2026-10-07 表同口径）。
4. （可选，esl 侧）B3 缓解：esl.Clock settle 深度参数化——单列，不动内核。
若 1+2 后 8 核仍翻不了盘，profile 定量（run_delta/kick/gc_poll 占比）再定 3 期。

### 批次 4 — 功能完成度（按需排期，各项独立可并行）
D1（挂起诊断）→ D2（libmeta effects）→ D3（多写者合并）优先（用户可见价值）；
D4（双域 GC）批次 3 后看 profile；D5/D6/D7 按需。

### 批次 5 — 定稿与默认迁移
E1 按门槛提请决策；E2 文档定稿（性能报告 + specifications/ 迁移 + EN 镜像）。

## 四、门禁纪律（沿用既有）

每项调度器内核改动：全矩阵 + `make bootstrap` 收敛 + TSan N=1/2/4 + tinyriscv N8 锤 +
交错 A/B 基准；期间发现的任何可复现 bug **立即** `Tests/<Category>/<Name>.md` 持久化
（修复断言正向 / 未修红哨兵，规则见 AGENTS.md）。

## 五、风险

- 批次 3 是内核协议大改，唤醒归档协议刚经历五连环洞修复——必须以批次 2 的 CI 网 +
  完整门禁兜底，小步提交。
- 批次 1.2 的长锤若仍偶发差一拍：按已定处置文档化（§7.1 契约本就只承诺边界后可靠），
  不追成阻塞项。
- E1 提升默认会翻转大量非 mt 测试的期望（13.2 审计成本）——必须 CI 网先行。
