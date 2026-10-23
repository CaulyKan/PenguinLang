# GC mt 隐性停顿与收集器策略（round 19+ 工作计划）

> **⛔ 已回退（2026-10-23，round 20）**：双层堆 + 并行 GC + 域本地收集的探索已整体撤销，
> 代码回到 `88c7cd65`（native arrays 收尾），仅保留对 classic 单堆收集器成立的少数修复
> （发射器帧解链、poll/profiling 降本，见回退分支 `feature/gc-rollback-to-classic`）。
> 本文档与 `gc-mt-stall-and-collector-policy.md` 从此只作探索档案：结论是"地基健全但
> 两大赌注均净负，且 bug 面大"（§round 18/19 数据），重启任何一条线前先读 §10。


> 前作：`gc-two-tier-heap.md`（双层堆设计 + round 3–18 排查/优化档案）。
> 本计划承接 round 18 五场景矩阵（`bench/gcmat/`）的全部结论，是下一阶段的唯一工作文档。
> 基线提交：`0c2b511a`（bench/gcmat + round 18 评估）。
>
> **round 19 实施记录见文末 §10**：M1 已结案——"~100ms/周期隐性停顿"的根因是
> 闸门升起窗口内的 poll 慢路径（每周期约 1200–2500 万次命中），与并行标记、
> gather 停靠、清扫分片都无关；M2 已落地（串行标记默认）；§1.2 的旧数字是在
> 被 profiling 自身污染的测试床里测的，读之前先看 §10.1。

## 0. 本计划要解决什么

矩阵已经证明：双层堆**地基健全**，但两个大赌注（默认开的并行标记、域本地收集器）
当前都是净负收益；mt 真正的瓶颈是**每周期 ~100ms 的账外停顿**和 **4× 堆目标超额**。
本计划按与主人确认的优先级（2026-10-23）推进：

1. 调查并压缩 mt 额外停顿（先插桩拆账，再对症下药）
2. 并行标记降级处置：默认翻串行，drain 部分待停顿调查结案后决定是否物理删除；
   gather 与并行清扫分片保留
3. 本地收集器改造：配额 live 比例化为先、上次回收效果反馈为辅；
   其价值主张与 M3 结论挂钩，**M3 若把全局周期修便宜则 M5 可能整个取消**

## 1. 现状事实（2026-10-23，round 18 收口时）

### 1.1 代码现状

- 分支 `feature/compiler-perf-hotspots`，工作树干净（除文档）。
- 运行时：双层堆（span 23 级 + malloc 大对象路径）、写点提升（`__gc_promote` /
  `array_set_promote`，单域早退）、并行全局回收（`EMPEROR_GC_PAR_MARK` 默认**开**，
  标记 drain + 清扫 malloc 分片同门）、域本地收集（`EMPEROR_GC_LOCAL`，单位 span≈8KiB，
  **mt 下被 `LOCAL_MT` 守卫关掉**）。
- `gc_profile` 已有 mark/sweep 分相计时（本轮修复：此前从未累加）。
- 老基线可比：`EMPEROR_GC_REGION=0` 在**同一二进制**内还原单堆老 GC。
- 测试资产：`bench/gcmat/`（五场景 + matrix.sh + aggregate.py +
  results-20261023.tsv，全部确定性 checksum）。

### 1.2 性能矩阵核心数字（紧凑堆 `EMPEROR_GC_YOUNG=8MB`，中位数）

**双层地基零开销（classic，全场景）**：OLD ≡ TWO 逐字节同级
（local 2606/2595、shared 2146/2144、live 1500/1497、large 2910/2941、app 2362/2363ms）。
提升实测工作正常：shared mt `promote_calls=1.92M`；classic 单域早退 0 开销。

**mt 隐性停顿**（隐性 = 开GC墙钟 − NOGC墙钟 − gc_total）：

| 场景 mt4 | 开GC | NOGC | gc_total | 隐性合计 | 周期数 | 摊/周期 |
|---|---|---|---|---|---|---|
| tbm_local | 7680 | 4961 | 155 | **2564ms** | 27 | **~95ms** |
| tbm_shared | ~5587 | 4199 | 94 | **~1294ms** | 9 | **~144ms** |
| tbm_live | 4949 | 2680 | 1389 | **~880ms** | 11 | **~80ms** |
| tbm_app | 2995 | 2870 | 23 | ~102ms | 6 | ~17ms |

classic 同减法 ≈ 0 甚至为负（回收热槽位反而更快）→ 这是 **mt 特有**成本。
特征：每周期近似常数（80–145ms），与周期工作量无关 → 瓶颈在等待不在干活。

**并行标记（PAR_MARK=1 默认）**：
- 链式 live（tbm_live）：mt4 mark 1317ms vs 串行 229ms（**5.7× 慢**），墙钟
  4949→3916（慢 26%）；mt8 同样慢 36%。机理：链只能串行遍历，4 worker 抢
  `_gt_qmu` 队列 + gray 位图 cache line；逐槽原子操作吃掉了串行位图差分扫描的批量优势。
- 宽图（tbm_shared，干净复测 3v3）：TWO 5587 vs SER 5951，**稳定 +6%**。
  归因未验证——候选：gather 主动邀请停靠缩短门开启窗口 / 并行清扫 malloc 分片。
- 其余场景无差异。**全矩阵唯一系统性危害来自它。**

**并行标记理论收益上限**：串行 mark 总量 local 21 / shared 6.7 / live 229 /
large 2.5 / app 1.9ms → 完美 4× 全场最多省 **~196ms**；隐性停顿全场合计
**~4.8s** → 差 **~24×**。这就是"收益空间差一个数量级"的出处。

**本地收集器（EMPEROR_GC_LOCAL）全矩阵无一胜绩**：
- live 重（tbm_live classic）：L16 **16702ms（11.2× 灾难）**——每 128KiB churn
  重标 12.6MB 森林 ×8161 次；L64 4947ms（3.3×）。
- 纯碎屑（tbm_local classic）：L16 3211 vs TWO 2595（+24%）——全局已被优化得太便宜
  （163 fulls 共 60ms），固定小额周期 ×17006 次怎么摊都亏。**回收率近 100% 也输**，
  说明问题在"频率 × 固定成本"，不只是效果。
- shared classic：L16 +46%。app/large：无差异。

**mt 堆目标超额**：触发前 live_peak 16.1MB vs classic 4.0MB（**4×**），fulls 11 vs 79。
机理未定位，候选：多竞技场并发分配下目标检查点稀疏 / 回收时刻 live 混入在途垃圾
撑大 goal=2×live+young / 4 原生栈保守扫描假留活对象。
超额与隐性停顿互相纠缠：周期变少反而掩盖每周期亏损（用内存换时间），单次停顿更胖。

**其它事实**：
- 默认旋钮（young=128MB）下所有场景 GC 占比 <5%，配置选择无差异——上一代 tb
  全平的根因（总分配不足一次触发线）。
- mt 内核本身：CPU 密集协程比 classic 慢 1.7–1.8×（NOGC 地板 4961 vs 2991、
  2680 vs 1493）；分配吞吐型（large）反而快 3.3×（894 vs 2941）。
  与 GC 无关，**另立计划**，本计划不动。
- 测 GC 前先看 fulls 计数：fulls=0 或 <10 的格子说明没触发，数据无意义。

### 1.3 正确性现状（比性能优先）

1. **mt 帧链竞态**（哨兵 `Tests/MtSchedulerTest/MtGcFrameChainRace.md`，6c7d16bb）：
   mt + 频繁 GC + 长寿命轮转 `array<u8>` 帧槽 + 多生产者 Fifo → 两种崩法：
   exit 134（`CORRUPT REF-MAP ctx=frame-mark`，map 恒指协程栈顶 `0x…fffffba0`，
   即被复用/munmap 的协程栈被当作活链走）或 exit 63（丢唤醒 `at=NOWHERE`）。
   **布局敏感海森 bug**：重链前 9/9 崩、分相计时重链后 0/10、8T 不绑核 50–85%/次；
   矩阵 mt 格散布 MIXED(n crash)。classic 与 NOGC 全绿。
   嫌疑：scheduler.c `parked_link` 窗口（switch 返回→重新挂链之间链只存在于
   `co->gc_frames`）/ epoch gather 覆盖缺口 / 销毁协程悬挂链。
2. **LMT（多域本地收集，实验开关）取证升级**：不止丢交接——tbm_live 三次全错
   checksum（**常驻森林也被收走**）；shared/app 直接崩。守卫保持默认关是正确的。
3. 编译器缺陷（已落哨兵）：构造器内显式泛型实参不特化
   （`ExplicitGenericArgInConstructor.md`，本轮新增）；async 参数带 `array<T>`
   不代入（`AsyncArrayParameter.md`，既有）；monomorphize 对泛型方法体内
   `__gc_promote` 钩的保留不稳定（round 12–13 档案，暂无用户可见影响）。

## 2. 全部结论

1. **双层地基（shared 位/域/写点提升）合理且保留**：关闭附加功能时零开销，
   提升在通道负载下正确工作，单域早退消除 classic 语义漂移。
2. **并行标记 drain 净负收益**：链式 live 5.7× 慢（现实负载主流形态），
   宽图仅 +6% 且归因未定；它是三轮崩溃的来源；理论收益上限（196ms）比
   隐性停顿（4.8s）小 24×。**但 gather 停靠聚合与并行清扫 malloc 分片需甄别保留**。
3. **本地收集器现状不可用、方向可救**：第一层修配额公式（live 比例化，套全局
   goal 同款摊销数学），第二层才是按上次回收 freed 比例反馈；还需最小间隔项。
   classic 上修好后上限≈与全局打平；**主战场在 mt（绕开 ~100ms 停顿税），
   但以正确性（帧链竞态 + LMT 暂存窗口）为先决条件，且价值取决于 M3 结论**。
4. **mt 隐性停顿是最大单点收益**：每周期 80–145ms 账外成本 = 周期本身计时的
   ~16×；机理候选：门未关窗口的慢路径轮询 + gather 等最慢 worker。目标超额
   （4×）与之配对——修便宜周期后可以又勤又快，峰值与延迟同降。
5. 默认用户体验由 young 余量主导（<5% GC 占比时算法选择无感）；本计划的收益
   兑现场景是紧凑堆/吞吐型部署——恰是 mt 的目标市场。

## 3. 已定决策（主人 2026-10-23 三点 + 修正共识）

| # | 主人原案 | 修正后共识 |
|---|---|---|
| 1 | 继续调查额外停顿 | ✅ 原样执行，第一优先级；**先插桩拆账再动手** |
| 2 | 删除并行标记，几乎不可能有收益 | 方向采纳，**拆开处置**：默认翻串行；drain 待 M3 结案后决定物理删除；gather 协同与并行清扫分片甄别保留（shared +6% 归因待查） |
| 3 | 本地回收按上次效果决定下次时机 | 方向采纳，**分两层**：①配额 live 比例化（数学上必须，先消灭 11× 灾难）②上次 freed 比例反馈（第二层，处理过渡期）+ 最小间隔；主战场 mt，排在正确性之后 |

## 4. 里程碑

### M1 插桩拆账（先行，小改动）
- gc_profile 增加：`gather_ms`（请求→全员停靠等待）、边界空闲计数、
  门未关窗口的慢路径命中计数（按周期聚合）。
- 用 tbm_local/tbm_shared/tbm_live mt4 复跑，把 ~100ms/周期拆成
  "等最慢 worker" vs "门未关慢路径" 两块。
- 顺带验证 shared +6% 归因：临时编译开关分离 gather-only / sweep-shard-only。
- 产出：数据写回本计划 §1.2，结论指导 M3 选型。

### M2 并行标记降级（一行默认 + 文档，立即可做）
- `EMPEROR_GC_PAR_MARK` 默认 1 → 0；AGENTS.md / gc.c 注释 / 哨兵说明同步。
- 保留代码路径与 env 开关；**不做物理删除**（等 M3 结案）。
- 验证：bench/gcmat 全矩阵复跑（预期 live mt 墙钟 -26%/-36%）、
  `make bootstrap` 一次重收敛（编译器自身是链式堆，串行标记预期也更快）、
  按记忆跑 release_win 交叉验证（改 C 运行时必验）。
- 注意：默认翻转改变 pass2..5 md5，属预期一次性重收敛。

### M3 停顿根因与缓解（本计划主战场）
- 按 M1 拆账结果二选一或并行：
  - **delta 时长上限**：协程每执行 N ms 强制边界停靠 → 收集延迟钉死在
    上限+周期耗时，同时超额因触发及时而收窄；
  - **门未关路径瘦身**：闸门升起期间轮询/分配保持单读快路径；
  - **分配慢路径对账目标**（不等轮询）+ 每竞技场配额（goal/N 提前升旗）。
- 目标：隐性停顿 <10ms/周期，live_peak 超额 <1.5×。
- 验证：bench/gcmat 矩阵 + tbm_local mt 的（开GC − NOGC）差值核算。

### M4 正确性前置（可与 M1–M3 并行推进）
- 帧链竞态根因（哨兵 MtGcFrameChainRace 转 Green 的路径）：
  优先排查 parked_link 窗口与 epoch gather 覆盖；插桩手法可用
  8T 不绑核高触发环境（50–85%/次）。
- LMT 暂存窗口（round 15/16 档案续查）：本地收集误收常驻数据的路径。
- 两者都是 M5 在 mt 上线的前置条件。

### M5 本地收集器改造（**M3 结案后重估必要性，可能取消**）
- 配额公式：`budget_bytes = live × factor + slack`（域内 live，套全局同款
  摊销数学），替代固定 span 数；feedback 项按上次 freed 比例调 factor。
- 验收：classic 全场景 ≥ 与全局打平（不再有 +24%/11× 一类格子）；
  mt 场景（守卫解除后）对全局有明显优势才考虑转正，否则维持默认关。
- 若 M3 把全局周期修到 <10ms 隐性停顿，本地收集器的 mt 动机大幅减弱，
  **届时评估直接退休该功能**（删代码比养代码便宜）。

## 5. 验证方案

- 每个里程碑：`bench/gcmat/matrix.sh` 全矩阵复跑 + aggregate.py 出表，
  与 results-20261023.tsv 对比；checksum 全对是硬门槛。
- 哨兵：MtGcFrameChainRace（应转 Green）、ExplicitGenericArgInConstructor、
  AsyncArrayParameter（不受本计划影响，防意外转色）。
- 套件：GcTest / AsyncTest / MtSchedulerTest；改 C 运行时必跑
  `make -C EmperorPenguin/std/c` + gc_torture（ASan 与非 ASan）+ CROSS=win64
  （见记忆 gc-runtime-edit-workflow）。
- M2/M3 触及运行时默认：`make bootstrap` 重收敛 + release 路径抽查。

## 6. 风险与回退

| 风险 | 缓解 |
|---|---|
| 帧链竞态是海森 bug，修不动/难复现 | 哨兵已钉（8T 高触发）；M4 独立于性能线，不阻塞 M1–M3 |
| delta 时长上限给 CPU 密集协程加停靠开销 | N 可调（env）；用 tbm_local NOGC 地板量化纯开销，>2% 即调大 |
| PAR 翻默认后某真实负载（如编译器自举）反而变慢 | bootstrap 计时对比纳入 M2 验收；预期更快（链式堆），意外变慢即回滚 |
| M5 改造后 classic 出现新语义漂移 | 单域早退 + classic 全绿矩阵是回归线；OLD≡TWO 逐字节同级必须保持 |
| 改 C 运行时期间 bootstrap 在后台跑 | 遵守记忆 gc-runtime-edit-workflow：后台跑时不编辑 std/c |

## 7. 非目标

- 分代回收、并发标记（mutator 与标记并行）、增量标记——维持前作非目标。
- mt 内核 CPU 密集负载比 classic 慢 1.7–1.8× 的问题（调度器课题，另立计划）。
- classic 单线程默认行为不因本计划改变（OLD≡TWO 同级是回归线而非改动对象）。
- 编译器显式泛型实参三处缺陷（initial/构造器/async 参数）——已哨兵化，
  修复归编译器轮，不混入本计划。

## 8. 与既有工作的衔接

- 前作档案：`gc-two-tier-heap.md` §round 3–18（优化轮、mt+LOCAL 十七轮、
  classic 回归修复、全矩阵终态）——本计划 §1 的事实全部可溯源到那里和
  `bench/gcmat/results-20261023.tsv`。
- 记忆：gc-matrix-verdict（矩阵结论）、gc-runtime-edit-workflow（改运行时纪律）、
  gc-torture-aslr-fragility（跑 torture 前置）、ep-clang-optimize-default（win 验证）。
- 哨兵索引：MtGcFrameChainRace（M4 目标）、LMT 相关档案在前作 round 14–16。

## 9. 待决策问题（需要主人拍板）

1. **M3 结案后并行标记 drain 的最终处置**：物理删除 vs 永久 env 隐藏。
   倾向删除（git 可溯），但若 M3 的解法复用其 worker 协同基础设施则保留。
2. **M5 是否值得做**：完全取决于 M3 结论——全局周期修便宜则本地回收整体退休
   （推荐），修不便宜才投入改造。
3. **mt 内核 CPU 慢**是否排期（本计划非目标，但影响 mt 画像的解读）。

## 10. 实施记录（round 19，2026-10-23）

### 10.1 先修测试床：profiling 本身在改程序

`GC_PROFILE=1` 把 `GC_GATE_TIMING` 放进 poll 闸门的静态位（每线程每次安全点都走
慢路径），并让每次分配做两个共享原子自增、每次 poll 做一个共享原子自增。实测
（tbm_local mt4）：

| 配置 | 墙钟 |
|---|---|
| NOGC 不 profile | 767 ms |
| NOGC + GC_PROFILE | 7721 ms（同一二进制） |
| 开 GC 不 profile | 3248–3365 ms |
| 开 GC + GC_PROFILE | 7116–7938 ms |

所以 §1.2 的"隐性停顿 = 开GC − NOGC − gc_total"是在**两个都被污染**的格子里做的
减法；差值恰好接近真值纯属两边的污染部分抵消。修正：

- 每次分配的两个共享原子 → **per-arena 计数**（`GtArena.prof_allocs/bytes`），
  退出时折叠（gc_span.c / gc.c 大对象路径）。
- 每次 poll 的共享原子 → **采样**（1/4096，按倍数记账）。
- `GC_GATE_TIMING` **删除**：profile 不再让 poll 常驻慢路径。
- 新增可复算的分项：`gate_wait_ms/gate_cycles/gate_hits`（请求→实际开收的窗口、
  窗口内慢路径命中数）、`gather_ms/par_own_ms`（并行相位会合窗口 vs 调用者自身
  工作量）、`boundary_ms`（边界等 idle 散兵）、`gather_parked`（从 between-delta
  停靠被唤醒的 worker 数）。`matrix.sh` 已采集这些列。

修正后 `GC_PROFILE=1` 的**结构性失真**（poll 常驻慢路径 + 每分配/每 poll 共享原子）消失；
剩下的是每分配两次 arena 计数写（分配密集的 NOGC 格约 +30% 墙钟）。因为开/关 GC
两侧按相同分配次数付同样的价，差值（本计划关心的量）仍然可比。

### 10.2 M1 结论：隐性停顿 = 闸门升起窗口内的 poll 慢路径

拆账数据（tbm_local mt4，YOUNG=8MB，修复前 + 只加插桩）：

```
fulls=25  gc_total=154ms  gate_wait=2347ms  gate_hits=124M  gather=157.5  par_own=157.4
```

- `gather_ms ≈ par_own_ms`（差 <0.1ms）→ **等最慢 worker 的暴露等待 ≈ 0**；
  `boundary_ms ≈ 0.15ms/周期`。§1.2 猜测的"gather 等最慢 worker"不成立。
- 真正的账：每个周期约 250ms 的闸门窗口（由 `EMPEROR_GC_STW_MS` 升级阈值决定），
  窗口内每个安全点都走 poll 慢路径，一个 tbm_local 进程 **1.24 亿次命中**。
  每次命中原先要做：采样计数（共享 RMW）+ 写一次共享的
  `_emperor_gc_last_collect_site`（4 线程 cache line 乒乓）+ 函数调用
  `parallel_active` + TLS 年龄 tick。≈16ns/次 × 1.24 亿 ≈ **2.0s**，
  与观察到的隐性停顿 2.35s 吻合。
- 另一处放大器：`gc_request_collect()` 每次分配（只要越过 goal）都无条件写
  `want_collect` + `poll_gate` 两个共享字。

修复（`gc.c` / `gc_internal.h`）：

1. `gc_request_collect()` **幂等化**：请求未清时直接返回（一次共享读代替两次写）。
2. 闸门窗口内的 poll 慢路径只保留**限速的 STW 年龄检查**（1/4096），诊断写点、
   gate_hits 采样全部并入同一限速块；非并行时仍走原逻辑。
3. 10.1 的 profiling 降本。

**效果（tbm_local mt4，不 profile，中位数）**：开 GC 3248–3365ms → **758ms**；
同机 NOGC 748ms → GC 的净成本从 ~2.5s 降到 **~10ms（噪声内）**。tbm_live：
411 vs NOGC 373（+38ms，gc_total ~108ms）；tbm_shared：1756 vs 1564（+192ms，
主要是写点提升，见 §10.6-2）。`gc_total` 仍计 180ms（local），但回收复用的
内存让 mutator 侧更快——与 classic 上"开 GC 比 NOGC 快"同因。

### 10.3 M2：并行标记默认翻串行（保留代码与开关）

`EMPEROR_GC_PAR_MARK` 默认改为 **0**（串行位图差分 drain）；`=1` 仍可开。
链条 live 上串行标记 35ms vs 并行 147ms（**4.2×**），墙钟 565 vs 694（mt4）/
565 vs 670（mt8）。**并行清扫分片与标记解耦**：原先 sweep 只在 mark gather
设置 `_gc_par_target>1` 时才分片，翻默认会连带关掉清扫并行；现在 sweep 自己
`gc_par_peek_target()` 决定分片，新增 `EMPEROR_GC_PAR_SWEEP=0` 单独关闭，
并提供 peek/gather 不一致时的串行兜底（防漏 free）。

**shared 的 +6% 归因**：round 19 复测 `SERSWEEP` vs 默认（并行清扫）在五个
场景都在噪声内（shared 1758 vs 1715–1784，且 mt4 三格 checksum 全对）。结论：
旧版的 +6% 是**旧的昂贵 poll 路径 + gather 唤醒**的交互（gather 会唤醒
between-delta 停靠者），不是清扫分片本身的收益。分片保留（默认开）但不再
声称收益；gather 基础设施保留给 M3 复用。

### 10.4 M3（停顿）达成，堆目标"超额"是误读

- 隐性停顿目标 `<10ms/周期`：**达成**（10.2，净成本≈0）。
- live_peak "mt 4× 超额"：**不是超额**。tbm_live 的每协程常驻森林 65536 节点
  ≈4MB；classic 调度器下 `wait` 把协程顺序跑完，任一时刻只有一个森林活着
  （live_peak 4.0MB）；mt 下 4 个森林真正并发（16.1MB = 4×4MB）。同场景 mt
  fulls=2（mark 35ms/次 ≈16MB）vs classic fulls=79（2.9ms/次 ≈4MB），
  GC 占比 19% vs 25%——mt 用更少的周期标更多活对象，效率不差。
  堆目标公式 `2×live + young` 没有失守；无需为"峰值得分"再加快周期。
- 未做：delta 时长上限。既然慢路径已便宜，缩短窗口只会让周期数×单个周期成本
  上升（估算 STW_MS=20ms 会让 tbm_local 的 gc_total 从 180ms 涨到 ~2s），
  故保留 250ms。

### 10.5 M4 进展：帧链崩溃的根因找到并修复（emitter 帧泄漏）

不是 scheduler 的 parked_link 窗口，也不是 epoch gather 覆盖。**根因在 LLVM
发射器**：

- `gc_frame_predicted` 在 scan 阶段预测"这个函数会不会链帧"，但帧槽还会在
  之后出现——入口参数装配（如 **native `array<T>` 参数**，预测无法分类它）和
  函数体里的 struct 临时 alloca。结果：这些函数**链上了 `%__gcf`，但每个 ret
  都没有解链**。
- 于是每次调用把死帧留在 `_emperor_gc_frame_head` 上；下一个函数复用该栈内存
  （布局不同）后，下一次回收走链就读到垃圾 `map` 指针（值落在协程栈/结构体上，
  正是 `0x…fffba0` 家族），或者反过来漏掉活根 → 丢唤醒。
- 取证：对哨兵的 `out.exe.ll` 做静态检查，找到 3 个"有 `%__gcf` 但 ret 块无
  unlink"的函数（`__array_fill/len/get$…`）。加入 `EMPEROR_GC_FRAME_ASSERT=1`
  后，崩溃信息会打印被遍历协程的 seq/状态/栈区间，确认头指针在栈内但内容已被覆盖。
- 修复（`LLVMEmitter.penguin`）：`emit_gc_frame_unlink` 改为**无条件发射**，
  `emit_gc_frame_descriptor` 返回 bool；函数体发完后若最终没有描述符，用
  `strip_gc_frame_unlinks` 把两行 unlink 从 body 里删掉。这样预测再也不影响
  正确性。

A/B 验证（8 线程、YOUNG=8MB）：

| 目标 | 修复前 | 修复后 |
|---|---|---|
| tbm_large mt8 ×6 | 2/6 exit 134（CORRUPT REF-MAP） | **6/6 通过** |
| MtGcFrameChainRace ×24 | 134/139/63 混合 | 0 次 134/139，仍 12/24 exit 63 |
| 哨兵 no-GC ×20 | — | 20/20 通过 |

**剩余（仍红的）exit 63** 是独立的调度器丢唤醒竞态：GC 关掉 20/20 全绿，
所以是 GC 停顿暴露了它，但已不是内存损坏——一个 CO_COND 协程既不在 runq/
owed/futures/timer/gc_valve/fd，也不在任何列表（`at=NOWHERE`）。修复路径：
`_mt_future_register` / park 窗口的注册-唤醒握手。哨兵描述已改写，明确区分
两种模式。

### 10.6 未做与遗留

1. **M5 本地收集器**：未动。矩阵仍是 live 重场景 11×（classic L16 16.2s vs
   1.6s），且既然全局周期已经便宜（tbm_local GC 净成本≈0），按 §4 M5 的
   退出判据，**建议直接退休该功能**（删代码比养代码便宜）——待主人拍板。
2. **mt 写点提升的开销**：tbm_shared mt4 默认 1715–1784 vs OLD（单堆、无提升）
   1268，NOGC 1618 → 提升路径约 +450ms（192 万次调用 / 864 万对象）。classic
   因单域早退为 0。这是双层堆在 mt 通道负载上的真实价格，round 20 候选：
   按 span 批量提升 / crit_arr 去原子化。
3. **丢唤醒竞态**：见 10.5 末尾；独立于性能线。
4. `EMPEROR_GC_PAR_MARK=1` 保留；M3 结案后按 §9 问题 1 决定是否删除 drain
   代码（gather/worker 基础设施被清扫分片复用，保留）。
5. 诊断工具（默认关闭）：`EMPEROR_GC_FRAME_ASSERT=1`（帧头栈内断言 +
   收集入口的 RUNNING 协程检查 + 崩溃时的帧链转储）。
