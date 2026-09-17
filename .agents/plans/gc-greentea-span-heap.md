# GC v3：Green Tea span 堆移植（已完成，M0–M4 全绿）

> 最终状态：greentea 为默认收集器；v2 分代机器已删除；
> barrier 发射已停用（ABI `emperor-rt-gc3-gt1`）。
> 量化成果（vs v2 基线）：mark 阶段 −79%~−99.8%，
> deepsurvive 总时长 4.8× 加速（9084→1896ms）。

分支 `feature/gc-greentea`。用 Go "green tea" 的 **span 批量标记**模型替换
`EmperorPenguin/std/c/gc.c`（GC v2，3611 行）的堆组织与 mark/sweep 核心。
**设计规格书（数据结构/算法/伪代码/函数清单）：
[docs/impl-notes/25_EmperorPenguinGC.md](../../docs/impl-notes/25_EmperorPenguinGC.md)**，
本文件只记计划与门禁，设计细节以规格书为准。

## 目标

1. **span 堆**：≤512B 槽位对象改从 8KiB span / size-class 分配（superchunk 复用
   v2 的 1MB 基础设施）；>512B 维持 malloc 块路径。
2. **green tea 标记**：工作单位从逐对象 worklist 换成 span——每对象 gray/black
   位存于 span 位图，`enqueued` 去重入队，representative 快路径，FIFO 出队后
   word-at-a-time `gray & ~black` 差分批量扫描。
3. **单代非移动**：删除分代 nursery（拷贝提升/tombstone/pin/chunk 降级）、
   card-table 写屏障；全死 span 整块回收（O(per-span)），finalizer 探测保留。
4. **量化验收**：指针密集/容器 churn 负载标记阶段 CPU −15% 以上；
   深存活负载回退 ≤5%。

### 非目标（规格书 §1/§16）

- 根发现机制（frame 链、全局根注册、tracked buffer、meta pin、quarantine ring、
  保守栈 cover）——全部保留不动。
- 发射侧 ABI：M1–M3 **零 .ll 变化**（barrier 调用降级 no-op 即可）；M4 才停发
  barrier 并 bump ABI tag。
- 并发/后台标记（单 mutator 运行时无收益）；SIMD 位图内核（后续迭代）；
  headerless 槽位（后续迭代）。
- BabyPenguin VM / `--backend=cs` 不受影响。

## 勘察结论（设计依据，2026-09 调研）

| # | 事实 | 出处 |
|---|---|---|
| F1 | Go green tea = 小对象堆标记重构（span 工作项 + gray/black 位 + enqueued 去重 + representative 快路径），非栈扫描方案；1.25 实验 / 1.26 默认，GC 开销 −10~40% | golang/go#73581；go.dev/blog/greenteagc |
| F2 | v2 已是精确 GC：精确 frame 链（`EmperorGcFrame`）、refmap 精确堆标记（`gc_refmap_walk`）、每次调用前 `_emperor_gc_poll`——根机制可直接复用 | gc.c:583-594, 1605-1713; LLVMEmitter.penguin:4475-4482 |
| F3 | 标记热循环是逐对象 worklist（push/pop 每可达对象一次）——green tea 的替换对象 | gc.c:1170-1189 |
| F4 | nursery = 64KB bump chunk × 1MB superchunk、128MB young budget；分代机制 ~1500 行（gc_minor/promote/pin/降级）是 M4 的删除面 | gc.c:695-721, 1210-1540, 2415-2769 |
| F5 | 单 mutator 单线程（无锁无原子；协程 = ucontext/fiber 切栈），STW 标记 ⇒ 无写屏障需求；v2 的 card table 只为分代 remembered set 服务 | gc.c 全文 grep；scheduler.c |
| F6 | 分配入口唯一：emitted 侧仅 `@_emperor_alloc_impl`（NEW/BOX 三处发射点）+ 字符串族 `_emperor_string_alloc`；JIT/dynlib 经 `-rdynamic` 绑定同一符号——改一处路由即可切换堆 | LLVMEmitter.penguin:4934/5123/5489/5507; penguin_jit.cpp:69-82 |
| F7 | 写屏障无条件发射于每个指针存储；运行时侧 no-op 化即可让旧 .ll 在 v3 下正确运行（反之新 .ll 去屏障只在 v3 运行时正确 ⇒ 需 ABI tag 门） | LLVMEmitter.penguin:4289-4318; gc.c:3474-3494, 3423 |
| F8 | finalizer（`dispose_mem`）承载 Vector/HashMap/Array 裸缓冲生命周期；alloc 时不知类型有无 destructor ⇒ 全死 span 整块回收仍需逐死槽探测 body word 0 | gc.c:1835-1850; vector.penguin:26-96 |
| F9 | `EMPEROR_GC_MODE` 多模式框架 + `EMPEROR_GC_STRESS_EVERY`/`GC_VERIFY` 差分机制已存在，greentea 模式与压测锤可直接挂接 | gc.c:3366-3414, 3094-3265 |
| F10 | gc_torture.c（746 行 standalone）+ Tests/GcTest 10 个 md 用例（env 驱动模式）为现成验收底座 | std/c/gc_torture.c; Tests/GcTest/ |

## 已确认决策（用户拍板）

1. **单代非移动**（Go 模型），不做分代混合。
2. **基准 + 量化目标**先行；SIMD 内核留后续迭代。
3. greentea 稳定后**切默认并删除** v2 分代代码（仅保留 `conservative` 应急回退）。

## 里程碑（每里程碑一次提交）

| M | 交付 | 门禁 | 状态 |
|---|---|---|---|
| M0 | `make gc-bench`：gc_torture 扩展 + `GC_PROFILE` 分相计时 + v2 基线数据表（记入规格书） | 基线表落盘；行为零变化 | ✅ 62e7d92 |
| M1 | span 堆**分配侧**（§4–§6）；标记暂以逐对象循环适配 span | gc_torture（含 STRESS_EVERY=1）+ `make test` 全矩阵 + `make unittest` | ✅ 739192d9 |
| M2 | **green tea 标记 + sweep**（§7–§9）挂接 `EMPEROR_GC_MODE=greentea`；GC_VERIFY 扩展 span 不变式 | 上列门禁 + GcTest × greentea 18/18 + bootstrap 收敛 lib md5 不变（.ll 零变化） | ✅ 961d567 |
| M3 | heap goal 启发式（垃圾富集倍增，双侧）、`gc_gt_stats` 扩展、调优 | **量化门槛全部大幅超越**：mark −79~−99.8%，deepsurvive 总时长 4.8× 快（v2 9084ms → 1896ms）；mixed512 168→12 cycles | ✅（待提交） |
| M4a | 默认切换 greentea + flip 门禁修复（环队列 wrap+grow；enum 全局未扎根） | bootstrap 收敛（编译器自举全链 greentea）；make test 1563/0 | ✅ b3f06a0 |
| M4b | 删 v2 分代机器（−1965 行）；precise/legacy 模式删除 | 全套回归绿；bootstrap 收敛 | ✅ c76e210 |
| M4c | 停发 barrier + ABI tag `emperor-rt-gc3-gt1` | bootstrap 重收敛；make test 1563/0；unittest 473/473 | ✅ 0a7c3f0 |
| M4d | 平台门禁（CROSS=win64 ✅、aarch64 语法级 ✅-无 sysroot）、release/lsp/tools/test 全链、torture 精简（−348 行）、规格书 implemented、AGENTS.md 更新 | 全部完成 | ✅（本次提交） |

## 风险与回退

| 风险 | 缓解 |
|---|---|
| 深存活负载回退（nursery 拷贝红利消失） | M3 门槛拦截；调 goal factor / span 缓存；超限则暂缓 M4 切默认并回报数据 |
| 位图/标记正确性 | GC_VERIFY 不变式断言 + 保守 cover 保留 + STRESS_EVERY 压测 + 双实现差分（M2 期间 precise 与 greentea 并存可对比） |
| 碎片（部分存活 span） | 空 span 跨 size-class 重分类复用；`EMPEROR_GC_SPAN_CACHE` 归还策略 |
| 混合构建（新 .ll × 旧运行时） | M4 ABI tag bump 显式拒链（沿用 gc.c:3417-3430 机制） |
| M4 删除分代后需要回退 | M4 为独立提交，可单独 revert（保留 `conservative` 模式兜底） |

## 验证命令

```bash
make -C EmperorPenguin/std/c && ./gc_torture          # 快速正确性锤
make gc-bench                                          # 基准（M0 起可用）
make test TEST_ARGS="--filter GcTest/*"                # e2e（greentea 模式经 env）
make unittest && make bootstrap                        # 全矩阵 + 收敛
```
