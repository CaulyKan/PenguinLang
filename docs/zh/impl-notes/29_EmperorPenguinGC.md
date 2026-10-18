# 29. EmperorPenguin GC —— Green Tea Span 收集器

EmperorPenguin 运行时垃圾回收器是一个**单分代、不移动、stop-the-world
的标记/清扫收集器**，核心是 **green-tea span 堆**（Go 为小对象重做的标记
策略，golang/go#73581，
[`go.dev/blog/greenteagc`](https://go.dev/blog/greenteagc)）：标记器不再把
单个对象指针逐个入队，而是把**同一 size class 的整个 8 KiB span** 入队，
用线性、对预取友好的位图遍历完成扫描。

代码地图：

| 文件 | 内容 |
| --- | --- |
| `EmperorPenguin/std/c/gc_span.c` | span 堆：size class、superchunk 供给、每线程 arena、分配、O(1) 地址→span、灰/黑位图标记、清扫 |
| `EmperorPenguin/std/c/gc.c` | 根发现（帧链、全局、区域、meta 固定、隔离环）、精确 ref-map 遍历、带排序内部指针索引的 malloc 大对象堆、收集驱动、统一堆目标、硬上限阀、安全点轮询 |
| `EmperorPenguin/std/include/gc_internal.h` | 两个编译单元共享的内部接口（GCHeader、布局常量、`gt_*` API） |
| `EmperorPenguin/std/include/emperor_gc.h` | 公开运行时 API（`_emperor_gc_init`、根、缓冲区、轮询、自省） |

该收集器是树中唯一的收集器，没有模式开关。运行时 ABI 标签为
`emperor-rt-gc3-gt2`（见 §9）。

## 1. 堆布局

### 1.1 两个堆

| | 小对象 | 大对象 |
| --- | --- | --- |
| 分界 | slot = `24B GCHeader + body`，取整到 size class 后 ≤ 512 B | 更大的所有对象 |
| 存储 | 从对齐 superchunk 上切出的 8 KiB span（gc_span.c） | 挂在 `_emperor_gc_allocation_list` 上的 `malloc` 块（gc.c） |
| 内部指针解析 | O(1)：范围测试 + 8 KiB 掩码 + magic（`gt_slot_owner`） | 对增量维护的排序起始地址索引做二分查找（`gc_resolve_block`） |
| 标记颜色 | span 位图中的灰/黑位 | `GCHeader.marked` + 共享标记工作表 |

字符串走同一路由（slot 落进某个 class 时 `_emperor_string_alloc` 走
span 路径）。

### 1.2 superchunk → span

```c
#define GT_SPAN_SIZE    8192              /* one span, 8KiB-aligned        */
#define GT_SPAN_HDR     128               /* sizeof(GtSpan), fixed         */
#define GT_BITMAP_WORDS 4                 /* 4*64 = 256 slots max          */
#define GT_SUPER_SPANS  128               /* 128 * 8KiB = 1MiB payload     */
#define GT_SUPER_SIZE   (GT_SPAN_SIZE * (GT_SUPER_SPANS + 1))
```

superchunk 多分配一个 span 并在内部向上对齐（`gt_super_alloc_bytes` ——
不依赖 `aligned_alloc` 的可用性约束，所有平台行为一致）。**前 8 KiB** 放
`GtSuper` 簿记块；其余 128 个 span 恰好落在 8 KiB 边界上。

```
superchunk (GT_SUPER_SIZE = 8192*129 bytes, 8 KiB-aligned)
┌────────────┬────────────┬────────────┬────────────┬─────┐
│ GtSuper    │  span #0   │  span #1   │  span #2   │ ... │  (128 spans)
│ 8 KiB      │  8 KiB     │  8 KiB     │  8 KiB     │     │
└────────────┴────────────┴────────────┴────────────┴─────┘
                  │
                  ▼ span #k（一个 size class）
┌───────────────────────────────┬────────┬────────┬────────┬─────┬────────┐
│ GtSpan header (128 B)         │ slot 0 │ slot 1 │ slot 2 │ ... │ slot N │
│ class,nslots,bitmaps,flags... │ 32..512 B each                     │
└───────────────────────────────┴────────┴────────┴────────┴─────┴────────┘
```

### 1.3 size class

23 个 class 覆盖 32..512 B 的 slot 尺寸（Go 的表去掉低于我们 32 B 最小
header+word slot 的部分）：

```
slot sizes: 32 48 64 80 96 112 128 144 160 176 192 208 224 240 256
            288 320 352 384 416 448 480 512          (23 classes)
```

`gt_class_of[slot>>3]` 是一张构建一次的字节表（`gt_build_classes`），
slot→class 只需一次读取。每个 span 的 slot 数为
`floor(8064 / slot_size)`（例如 252 × 32 B、31 × 256 B、15 × 512 B）。

### 1.4 对象布局

每个 slot 以 24 B 的 `GCHeader`（`{next, marked, is_string, size}`）开
头，随后是 body，其第 0 个字是代码生成在分配后立刻盖上的
`EmperorClassMetadata*`（字符串则放 `_emperor_string_metadata*` + 长度 +
数据；`is_string` 阻止一切 body 遍历）。在 span 中 `next`/`marked` 是冗
余的（池链与颜色在 span 一侧）——这是接受的开销；无头部槽位是未来工作
（§11）。内部指针（+16 处的枚举 payload、嵌套值类）在两个堆上都以
**所属者** slot 为根，包含规则一致。

### 1.5 地址 → span（O(1)）

span 按构造 8 KiB 对齐，解析就是一次掩码加校验（`gt_span_of`）：

```c
static GtSpan* gt_span_of(const void* p) {
    if (!gt_in_super_range(p)) return NULL;            /* EXACT ranges    */
    GtSpan* s = (GtSpan*)((uintptr_t)p & ~(uintptr_t)(GT_SPAN_SIZE - 1));
    if (s->magic != GT_SPAN_MAGIC) return NULL;       /* false-hit guard   */
    return s;
}
```

`gt_in_super_range` 在排序的**精确 super 范围表**上二分查找（在
`gt_super_acquire` 时记录；单条缓存顶在前面——标记的引用流有很强的
span 局部性）。宽松的 `[heap_lo, heap_hi)` 范围会盖住 super 分配之间的
未映射间隙，而 `gt_span_of` 会对掩码后的基址**解引用**——落在间隙里的候
选值会在读 magic 时空指针崩溃。属于某个真实 super 即按构造保证解引用安
全（super 永不释放，范围只增不减）。magic 校验同时排除 super 的簿记块
（偏移 0 处的 `GT_SUPER_MAGIC` 不同，过不了检查）。

## 2. 分配

### 2.1 每线程 arena

分配状态是**每线程的**（`GtArena`，gc_internal.h）：每 class 的当前
span、线程的 fresh-object 隔离环、未上卷的 `alloc_bytes` 增量。arena 在
首次使用时注册进固定的 64 槽池（`gt_arena_thread`，持 `gc_alloc_lock`；
TLS 指针服务无锁快路径）。arena 比线程活得久：stop-the-world 清扫遍历注
册表，而不是活线程。

### 2.2 快路径

`_emperor_gc_alloc(size, is_string)` 把小对象路由到 `gt_alloc_small`
（gc_span.c），它：

1. 由取整后的 slot 尺寸算出 class；
2. 取 arena 的当前 span，从 `free_index` 提示开始扫 `alloc_bits` 找第
   一个空闲位；
3. 盖 header、清零 body、累加 `alloc_bytes`（arena 本地，无共享写）；
4. 把新指针压入 arena 的隔离环；
5. 检查统一堆目标（`gc_unified_goal_reached`），到达即抬升
   `_emperor_gc_want_collect`——幂等，直到某个轮询把它排干。

整条路径不加锁；arena 的当前 span 由本线程独占。

### 2.3 慢路径（退休 + 补充）

当前 span 没有空闲 slot 时，`gt_retire_refill`（持 `gc_alloc_lock`）
把 arena 的 `alloc_bytes` 上卷进全局近似计数器，把耗尽的 span 退休到共
享 full 链表，再取一个新的：共享 partial → 空池（按需重新分类，class
之间不会困住内存）→ 新 superchunk。superchunk 失败时在锁**外**跑一次应
急收集（收集期分配走 malloc 路径，而锁不可重入）并重试一次；并行 delta 进行中
跳过应急收集，改由边界收集（§7.3）。分配从不清除 alloc 位——只有清扫会
清——所以空闲 slot 只能在那次清扫之后复用。

### 2.4 malloc 大对象路径

大对象（以及小对象的落空情形：superchunk OOM、注册表耗尽、**收集期间**
的分配——进行中的清扫不能经 span 位图判定它们）走 `gc_alloc_locked`：
一块清零的 `malloc` 块挂上 `_emperor_gc_allocation_list`，追加进排序索引
的 pending 集（`gc_pending_append`），并压入 arena 的隔离环。

排序索引**增量维护**：分配只追加进一个小的 pending 集；每次收集只对这
个集合排序并回 merge 进持久的有序数组；清扫在同一趟里压实掉死条目
（`gc_refresh_sorted` / `gc_sweep_pass2`）。pending 追加失败会把索引标为
stale，下一次刷新整体重建——丢失条目会让标记释放活对象，因此该回退是
必须的。

### 2.5 硬上限

`EMPEROR_GC_HARD_CAP`（默认 8 GiB，0 = 不限）在长并行 delta 推迟收集
时给堆封顶：分配器拒绝这笔开销，触碰上限的协程挂到调度器的"等 GC"列表
（`_emperor_sched_alloc_park`——不可运行，恰好让全阻塞 delta 收敛到边界
的 stop-the-world 收集），空间释放后重试。没有协程可挂的地方（主栈代
码、classic 调度器）挂起立刻返回 0，目标节奏不变——阀无法起作用的地方
上限只是建议性的。超过上限本身的单次分配确定性地中止
（`_emperor_gc_oom_abort`，退出码 62）；边界的 OOM 判定
`_emperor_gc_cap_blocks` 比较 live + charge，parker 不会永远卡在两者之间
的缝隙里。

## 3. 根

| 根来源 | 机制 |
| --- | --- |
| 栈帧（所有栈） | `EmperorGcFrame` 链挂到 `_emperor_gc_frame_head`（thread-local；调度器按协程切换，sjlj 抛出时恢复）。每个 slot 要么是裸指针槽（`map == NULL`：`*addr` 是一个引用），要么是按其 ref-map 遍历的内联 struct 槽。调度器的 `_emperor_sched_each_frame_head` 枚举每个**停驻**协程的保存链。 |
| 全局 | `_emperor_gc_add_root(void**)` 注册表（注销时交换删除）；值类/枚举全局额外把整个 struct 注册为扫描区域。 |
| 原生数组 `array<T>` | 一等 GC 对象——无需注册。布局 `{ ptr metadata, i64 length, elements... }`；元素类型的 metadata refmap 是 REFARR 节点（kind 3：`elem_stride`、`elem_sub`），walker 从对象+8 读存活数、用 `elem_sub` 逐元素走（primitive 元素是精确 no-op，不是保守扫描）。容器（List/Vector/HashMap）现在由 `array<T>` 槽位承载，容器图经由普通对象标记精确追踪。 |
| 协程栈 | 由调度器注册为区域，经 `_emperor_gc_scan_set_live` 收窄到 `[parked_sp, top)`——停驻 sp 之下的死帧是保守扫描会永久钉住的陈旧垃圾，被排除；**运行中**协程的区域在收集时被 sp 封顶。 |
| meta 固定 | `_emperor_gc_pin_object`：以裸 i64 地址烘焙进 meta stub 的编译期对象值被固定为进程生命周期的根（整数对标记器不可见）。 |
| 隔离环 | 每个 arena 最近分配的有界环（512 项）——覆盖只被无家可归 SSA 临时值引用的新对象（`this.f = new Outer(new Inner(...))` 形态：Inner 结果在语句完成前没有 slot 也没有堆上的家，而两次分配之间就有轮询）。显式 `gc_collect()` 丢弃环（collect-now 语义——年轻垃圾必须死；在途临时值由下面的保守覆盖兜底）；自动周期消耗环。 |
| 保守栈覆盖 | 主栈的按字扫描——永远开启。在不移动收集器中安全：命中只是多一个标记，因此不存在固定机制。每个注册线程贡献 `[base, boundary watermark)`；收集线程自己的栈从 sp（含 setjmp 溢出的寄存器）扫到它的 watermark。 |

## 4. 标记

### 4.1 span 位图中的三色

slot 的颜色存放在其 span 的位图里——白 = 两位皆无，灰 = 可达/未扫描，
黑 = 可达/已扫描——于是设置/测试颜色是对 64 个相邻对象共享的 64 位字做
移位+或运算；标记期间没有逐对象的头部访问。`GCHeader.marked` 只在
malloc 大对象路径上继续使用（gc.c 中的共享工作表）。

### 4.2 标记叶子

每次根遍历把候选指针解析到所属者并置灰：

* `gt_mark_candidate(p)`（gc_span.c）是热路径叶子：一次范围查找同时决
  定 span 还是大对象——成员判定、所属者解析与置灰合并在一起，因为拆开
  的形式每个引用字要付两次 super 范围二分查找；
* 非 span 候选回退到 `gc_resolve_block`（排序索引）并逐对象标记+入栈；
* 持有精确用户指针的根遍历走 `_emperor_gc_mark_object` →
  `gt_gray_object`。

### 4.3 span 置灰：队列 + 代表对象快捷路径

`gt_gray_slot` 在灰位上去重，然后做代表对象簿记：

* 出现**第一个**灰对象的 span 完全不入队——它带着 `rep_slot` 停在代表
  数组上，孤对象在排空时被直接扫描（`n_rep_scans` 计数命中；真实堆中
  很多 span 每轮恰有一个存活对象）；
* 出现**第二个不同**对象时翻起 `REP_HIT` 并把 span 入队做真正的位图扫
  描（FIFO 队列用 `GT_F_ENQUEUED` 去重）。

代表对象簿记用的是数组而非借 `GtSpan.next` 的链表：标记期间 span 的
`next` 是它的**池**链（current/partial/full），不能再兼任标记结构链。span
离开两个结构后再次置灰的候选，会先清掉陈旧的 `REP_HIT` 再重新 pend。

### 4.4 排空循环

`gt_drain` 跑到全局灰 == 黑：先大对象工作表（其遍历会给 span slot 置
灰），再队列中的 span，再孤立代表对象。一次 span 扫描
（`gt_scan_span`）逐字计算 `gray & ~black`，整批置黑，恰好扫描这些对
象；扫描可能给同一 span 置灰新 slot，所以扫描反复重过直到该 span 局部
安静。队列是 FIFO，span 大致按地址升序扫描——缓存未命的减少正是
Green Tea 得名的机制。

对象 body 经每类型 **ref-map 程序**精确扫描（`gc_refmap_walk`；编码见
`emperor_types.h`）：只读取发射器声明的指针位置。无 ref-map 的 body
（外来元数据，或清零后、元指针盖上前的新块）回退为保守的整 body 按字
扫描。损坏的 ref-map 程序（偏移越界、枚举 tag 超出变体表、REFARR 范围
超出对象）带 forensics 地大声中止。`elem_sub == 0`（无指针元素）的 REFARR
节点从不读元素区——备用容量槽位对 marker 不可见。

**失败策略**：标记中途任何工作表/队列增长失败即置
`_emperor_gc_mark_failed`，标记不完整，该周期保留一切（不清扫）——下一
周期从头重判。

`GC_VERIFY=1` 额外断言每次排空后的 span 不变式
（`gt_verify_invariants`：逐字灰 == 黑、黑 ⊆ alloc），并对每个无
ref-map 的类型名警告一次。

## 5. 清扫

成功的标记之后，黑 = 存活。finalizer 已不存在（IMemoryDispose/destructor
机制随原生数组一轮退役——文件显式关闭，容器是 GC 追踪的 `array<T>` 对象），
清扫完全不运行用户代码，也不需要阶段排序：

1. `gc_sweep`（gc.c，单遍）——遍历 malloc 链表，把死块摘成 pending 链
   （墓碑化为 `marked == 2`），同遍压实排序索引，然后 `free()` 该链；
2. `gt_sweep_reclaim`（gc_span.c）——清除死的 alloc 位，重算存活计数，
   重新入池：`live == 0` 时**整体**回收到跨 class 的空池（婴儿死亡率型
   负载大多产出死 span，回收成本从 O(对象) 降到 O(span)），否则进
   partial/full。

收集期分配走 malloc 路径（collecting 锁存抑制嵌套收集），不会被本周期
的摘链遍历看到。

## 6. 收集周期

`gc_collect_greentea`（gc.c）是唯一的驱动器；所有入口——轮询排干、压力
收集、应急慢路径、显式 `gc_collect()` 与调度器边界——都汇入
`gc_collect_auto`：

```
gc_collect_greentea():
    守卫: disabled / no stack / already collecting
    setjmp 寄存器冲刷; 捕获 sp
    gc_refresh_sorted()                     # 增量索引合并
    标记: 全局根 → meta 固定 → 帧链(运行中 + 停驻)
          → 区域(保守按字扫描)
    gc_mark_drain()
    隔离环: 显式周期丢弃环, 自动周期消耗环
    保守栈覆盖(本线程 watermark + 其余每个注册线程的停驻范围)
    gt_drain()                              # 到灰 == 黑
    [GC_VERIFY] gt_verify_invariants()
    标记失败: 重置所有颜色, 保留一切, 返回
    清扫: gc_sweep（摘链+压实+释放）→ gt_sweep_reclaim
    gc_update_unified_goal(freed)
```

协程安全：收集只发生在运行线程的安全点轮询内（或在每个 worker 停驻的
调度器边界）；其余所有栈都处于停驻状态，帧链完整可枚举，扫描区域收窄到
`[parked_sp, top]`。

## 7. 触发与统一堆目标

### 7.1 目标公式

一个预算同时覆盖两个堆（触发比较 malloc 已分配 + span 已分配字节数，
清扫后的更新从**合并**存活集重算）：

```
goal = max(EMPEROR_GC_MIN_HEAP, EMPEROR_GC_GOAL_FACTOR × combined_live)
       + EMPEROR_GC_YOUNG
```

* 比例基（默认因子 2）是经典的 2×-live 阈值；
* 加性的 young 预算（默认 128 MiB——编译器自举上测得的拐点）是两次收
  集之间有保证的搅动余量：纯粹的 factor × live 目标会把垃圾比例每轮钉
  在 1/(1+factor) 附近，攀升中的存活集要按攀升积分付出代价；
* 存活沉重的清扫（freed < live/4——LSP 重编译形态，每轮付出 O(存活块)
  却几乎不释放）把目标 ×4，上限 4 TiB；
* 目标从不收缩。峰值堆被限制在 (1+factor) × live + young。

### 7.2 常规触发：安全点轮询

发射器在每次调用前和每个分配点放置 `_emperor_gc_poll`。稳态路径只读
**一个词**——轮询门（poll gate，位定义在 `gc_internal.h`）：零即返回，
非零才进慢路径。凡是"这次轮询该做点什么"的条件都折进这一个字：收集请
求经 `gc_request_collect()` 统一抬升（语义标志 `_emperor_gc_want_collect`
与门同步），stress / 轮询计数这些 env 派生的位在 init 点亮，收集驱动每
轮降门，STW 年龄时钟的重新武装也一并搬进驱动——于是快路径是一次宽松读
加一个不跳转的分支，连一个 store 都没有；此前同一处要读五个不同的全局
量。`_emperor_gc_want_collect` 被抬升时（任一分配侧越过目标）运行完整
周期。压力旋钮（`EMPEROR_GC_STRESS_EVERY` / `_MAX`）强制每第 N
次轮询收集一次，用于正确性锤击。malloc 路径在 8× 目标处额外内联收集
——应对没有轮询在望的 C 密集段落。

### 7.3 并行 delta 与边界

mt 调度器运行并行 delta 期间（`_emperor_sched_parallel_active`），
worker 持有飞行中的协程栈，任何线程都不能收集：分配侧触发只抬标志，轮
询推迟，**调度器边界**（每个 worker 停驻——按构造即 stop-the-world）经
`_emperor_gc_collect_now` 排干。边界排不掉的标志（永动 delta 里的繁忙
服务）在 `EMPEROR_GC_STW_MS`（默认 250 ms，0 = 关闭）后升级：轮询中的
协程把自己挂到硬上限阀的停驻列表，强制边界需要的收敛。年龄时钟在首次看
到标志的轮询处启动，标志落下即重置；重置是条件写，因为排干后的稳态是每
个协程每次回边的热路径。

## 8. 自省

| 符号 | 含义 |
| --- | --- |
| `gc_collect()`（penguin 内建） | 显式完整周期——"collect now" 语义：隔离环被丢弃，保守覆盖开启 |
| `gc_info()`（penguin 内建） | 精确的管理字节数（malloc 链表 + span 堆，含未上卷的 arena 增量） |
| `gc_gt_stats(which)`（penguin 内建） | 0 存活 slot 字节，1 目标字节，2/3/4 full/partial/empty span 数，5 整体回收的 span（累计），6 代表快捷路径扫描（累计），7 完成周期数，8 superchunk 数。`Tests/GcTest/GreenTeaSpanStats.md` 端到端断言这些计数器。 |
| `_emperor_gc_info_split`（仅测试） | malloc 堆字节 vs span 堆字节 |
| `_emperor_gc_alloc_charge`（仅测试） | 一次被跟踪分配的堆开销（size-class slot，或大对象路径的 header + round8）——gc_torture 的精确存亡差值用它 |
| `EMPEROR_GC_STATS=1` / `GC_PROFILE=1` | 退出时的阶段/成本归因；profile 行供 `make gc-bench` 使用 |

## 9. 发射代码 ABI

* 每次调用与分配前的安全点轮询（`_emperor_gc_poll`）；每函数的帧链
  挂接/摘除；每类型的 ref-map 程序与元数据全局（数组含在内：每个元素
  类型一个 REFARR 节点）。
* 运行时 ABI 标签 `_emperor_runtime_abi == "emperor-rt-gc3-gt3"` 在混
  合 dynlib 构建上被比较，发射代码不会悄悄遇到外来运行时。
* JIT 模块与 dynlib 消费者经 `-rdynamic` 从宿主可执行文件绑定分配器/
  轮询/帧头符号；所有 `gt_*` 函数是运行时内部的。

## 10. 旋钮

| 旋钮 | 默认值 | 含义 |
| --- | --- | --- |
| `EMPEROR_GC_GOAL_FACTOR` | `2` | 统一目标对合并 malloc+span 存活字节的乘数 |
| `EMPEROR_GC_MIN_HEAP` | `4194304` | 统一目标的下限（字节） |
| `EMPEROR_GC_YOUNG` | `134217728`（128 MiB） | 目标的加性搅动余量；小内存主机可调低（下限 64 KiB） |
| `EMPEROR_GC_HARD_CAP` | `8589934592`（8 GiB） | 分配压力阀；0 = 不限 |
| `EMPEROR_GC_STW_MS` | `250` | 面向永动并行 delta 的轮询侧 STW 升级年龄；0 = 关闭 |
| `EMPEROR_GC_STRESS_EVERY` / `EMPEROR_GC_STRESS_MAX` | 关 | 每第 N 次轮询收集；M 次后停止 |
| `EMPEROR_GC_DISABLE` | 关 | 把目标抬到 8 TiB 使自动收集永不发生（诊断用）；显式收集照常 |
| `GC_VERIFY` | 关 | 每周期断言 span 不变式；对无 ref-map 类型告警 |
| `EMPEROR_GC_STATS` / `GC_PROFILE` | 关 | 退出时成本归因 / 机器可解析的基准行 |

`EMPEROR_GC_MODE` 已废弃并被忽略（设置时打印一条警告）。

## 11. 测试与基准

* **`gc_torture.c`**（独立程序；构建命令见其文件头注释）：排序索引的
  正确性段（内部指针、地址复用、校验和稳定），带精确存亡差值的精确
  ref-map 遍历（毒字、陈旧枚举 payload、tag 翻转），以及 green-tea 段
  （混合 span/malloc 图完整性、精确死槽回收差值、环队列 wrap+grow 回
  归、整体回收）。**harness 纪律**：精确差值断言只有在待释放指针的陈旧
  副本不残留在**活**帧中时才成立——保守覆盖读取活帧的每个字。因此后续
  断言要释放的指针，其分配、发布与丢弃都在 helper 函数内完成，帧死亡后
  被 scrub（`scrub()` + `CLEAR_CALLEE_SAVED()`）；单纯的死存储
  （`x = NULL`）不够——编译器会消除它。ASan 下用
  `ASAN_OPTIONS=detect_stack_use_after_return=0` 让局部变量留在真实栈上
  （假栈会让覆盖看不到它们）。
* **`make gc-bench`**（`EmperorPenguin/std/c/gc_bench.sh`）：release 构建
  gc_torture 并驱动六个确定性负载（churn / ptrdense / container /
  deepsurvive / mixed512），按负载制表 `GC_PROFILE` 阶段耗时
  行。
* **`Tests/GcTest/*.md`**：跨编译器端到端守卫——搅动下的枚举全局与
  payload、原生数组元素追踪、容器回收、写链/参数置根、span 统计
  自省。
* C 运行时默认 `-O2` 构建（std/c Makefile 的 `OPT`；分配器与调度器的上
  下文切换是热路径）。
* 平台：linux x86_64 + aarch64，`CROSS=win64`（llvm-mingw）。仅有的架构
  敏感点是栈指针 asm 和计时时钟（QPC vs `clock_gettime`）。

## 12. 未来工作

- **SIMD 位图内核**：用 AVX-512/GFNI 块计算 `gray & ~black`，CPUID 门控
  加标量内核回退。
- **无头部槽位**：从 span 推导尺寸/class——每
  对象收回 24 B。
- **本地/共享域拆分**：调度器交接点上带晋升的每 arena 本地收集器，取代
  对所有 arena 的一次全局 stop-the-world 遍历。
- **退役安全网**：隔离环与保守栈覆盖可以在 `GC_VERIFY` 于全集上长期跑
  干净后收缩。
- **并发标记**：只在多 OS 线程下有意义；会重新引入写屏障——不在范围内。

## 13. 参考

- golang/go#73581 — runtime: green tea garbage collector（设计与数据）：
  <https://github.com/golang/go/issues/73581>
- Go 博客：The Green Tea Garbage Collector：
  <https://go.dev/blog/greenteagc>
- 发射器侧：`EmperorPenguin/src/llvm/LLVMEmitter.penguin`（轮询发射、帧
  描述符、元数据/ref-map 表、分配点）
- 调度器互操作：`EmperorPenguin/std/c/scheduler.c`（边界收集、停驻栈枚
  举、alloc-park 阀）
