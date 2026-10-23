# gcmat — GC 性能矩阵测试床

五场景 × 调度器（classic / mt4 / mt8）× GC 配置（紧凑堆 / NOGC / RELAX），
测 classic 单堆绿茶收集器在真实负载形态下的墙钟、checksum 与分相计时。
聚合：`python3 aggregate.py`（读 `/tmp/gcmat/results.tsv`）。

> 历史存档：`results-20261023.tsv`（round 18）与 `results-20261023b.tsv`
> （round 19）是双层堆/并行 GC 探索时期的矩阵，其中 REGION/LOCAL/PAR_*
> 等旋钮已随探索一并回退（见 `.agents/plans/gc-mt-stall-and-collector-policy.md`
> §10 与回退提交），只作证据保留，列结构与现行 matrix.sh 不同。

## 场景（每个都自带确定性 checksum，跑错即 GC 缺陷）

| 场景 | 负载形态 | 隔离什么 |
|---|---|---|
| `tbm_local` | 4 协程纯私有垃圾（16 节点链，即建即弃），零共享 | 小对象 span 堆 + 纯垃圾回收节奏 |
| `tbm_shared` | 4 生产者→4 消费者背压通道 | 跨协程共享图的标记 + 调度交互 |
| `tbm_live` | 每协程 65536 节点常驻森林 + 高频碎屑 | 标记成本随 live 放大 |
| `tbm_large` | 192KiB 大缓冲轮转 + 32KiB 瞬态 + 小链 | malloc 大对象路径 + 增量索引 |
| `tbm_app` | 256 槽共享环缓存 + logger 通道 + 请求处理 + 64KiB 缓冲 | 综合真实形态 |

## 用法

```bash
# 编译两种变体（classic 单线程调度器 / mt 多线程内核）
for f in tbm_local tbm_shared tbm_live tbm_large tbm_app; do
  build/linux/emperor_penguin $f.penguin -o bin/${f}_c
  build/linux/emperor_penguin $f.penguin --enable-multithread-scheduler -o bin/${f}_m
done
./matrix.sh          # 全矩阵（classic/mt4/mt8 × TIGHT/NOGC/RELAX）→ results.tsv + stats.tsv
python3 aggregate.py # 聚合：results.tsv 的中位墙钟 × stats.tsv 的探针统计
```

所有计时格子 3 次取中位、**不带 GC_PROFILE**（profile 的每分配计数原子会拖慢被测对象，
产品墙钟必须在无 profile 下取）；fulls/mark/sweep/live_peak 来自每个 (场景,调度器) 一次
的不计时探针（stats.tsv）。NOGC/RELAX 等对照 1 次。默认紧凑堆
`EMPEROR_GC_YOUNG=8388608`（默认 128MB 余量下 GC 占比 <5%，任何配置都测不出
差异——这是上一代 tb 全配置无差异的根因）。测 GC 前先看 fulls 计数：fulls=0
或 <10 的格子说明没触发，数据无意义。

## 旋钮速查（现行 runtime）

- `EMPEROR_GC_YOUNG` / `EMPEROR_GC_GOAL_FACTOR` / `EMPEROR_GC_MIN_HEAP` 堆目标
- `EMPEROR_GC_DISABLE=1` 免 GC 地板；`EMPEROR_GC_STW_MS`（mt 边界升级阈值）
- `GC_PROFILE=1` 输出 `gc_profile:` 行（fulls/mark/sweep/gc_total/allocs/
  live_peak；计数为每线程 1/4096 采样 ×倍数，分配计数 per-arena 折叠——
  profile 不再把每次安全点拉进慢路径）
- `EMPEROR_GC_STATS=1` 人读版退出摘要
