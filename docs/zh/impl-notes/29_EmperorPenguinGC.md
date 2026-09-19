# 29. EmperorPenguin GC v3 —— Green Tea Span 收集器

> **状态：已实现（M0–M4 完成，分支 `feature/gc-greentea`）。**
> span 堆（`EmperorPenguin/std/c/gc_span.c` + `gc.c` 中的 greentea 驱动）
> 是默认收集器；v2 分代机器已在 M4b 删除（约 1550 行），写屏障发射也在
> M4c 退役（ABI 标签 `emperor-rt-gc3-gt1`）。标注了历史 `gc.c:NNN` 锚点的
> 小节指的是 v3 之前的代码树，作为设计依据保留。
> 实现计划与里程碑：`.agents/plans/gc-greentea-span-heap.md`。

Green Tea 是 Go 对**小对象堆标记**的重构（golang/go#73581，
[`go.dev/blog/greenteagc`](https://go.dev/blog/greenteagc)；Go 1.25 中为实验特性，
Go 1.26 中成为默认）：标记器不再把单个对象指针逐个入队，而是把**同一
size class 的整个 8 KiB span** 入队并按地址顺序扫描，使指针追逐变成线性、
对预取友好的位图遍历。Go 报告在 GC 密集的程序上 GC 开销降低 10–40%。
它*不是*对栈扫描的重构 —— EmperorPenguin 的精确式根机制（帧链、
ref-map、被跟踪缓冲区）已经存在并**保持不变**。

## 1. 范围

| | |
| --- | --- |
| **取代** | GC v2 的*堆组织与标记/清扫核心*：复制式 nursery（疏散/晋升/固定/墓碑，`gc_minor` gc.c:2415-2769）、作为*唯一*对象路径的 malloc 老年代、逐对象标记工作表（gc.c:1170-1189）、卡表写屏障（gc.c:898-925）。 |
| **保持不变** | 根发现：精确式帧链（`EmperorGcFrame`，gc.c:583-594；LLVMEmitter.penguin:1642-1667）、全局根注册表（gc.c:294-322）、被跟踪缓冲区区域（`_emperor_gc_track_buffer`）、meta 固定（gc.c:1085-1109）、隔离区环形队列（gc.c:638-650）、保守式栈覆盖。每类型 **ref-map 程序**与 `gc_refmap_walk`（gc.c:1605-1713）。每次调用/分配前的安全点轮询 `_emperor_gc_poll`（LLVMEmitter.penguin:4475-4482）。发射代码的符号 ABI（`_emperor_alloc_impl`、`_emperor_gc_frame_head`、屏障符号 —— 见 §11）。 |
| **收益** | 单分代、不移动、对分配局部性友好的 span 堆；批量化的 span 标记；对死亡 span 的 O(每 span) 整体回收；无需写屏障（STW，单赋值器）；在 M4 删除整个 nursery/固定机制。 |
| **不照搬 Go 之处** | 并发标记与调度器运行队列的工作分发（本运行时是单赋值器：用户级协程在一条 OS 线程上切换栈，见 scheduler.c）；无头部槽位（我们的对象保留 24 B 的 `GCHeader`，§4.4）；SIMD 位图内核（未来工作，§16）。 |

## 2. 背景

### 2.1 今天的 GC v2（被取代的部分）

| 机制 | 位置 | 说明 |
| --- | --- | --- |
| nursery：从 1 MiB superchunk 上切出的 64 KiB bump chunk，128 MiB 年轻代预算 | gc.c:695-721, 2250-2314 | 复制式：幸存者经墓碑晋升到 malloc 老年代（`gc_promote_young` gc.c:1511-1540）；仅被保守式字引用到的对象被**固定（pin）**，chunk 被降级 |
| 老年代：`malloc` 块挂在 `_emperor_gc_allocation_list` 上，外加按起始地址排序的索引用于内部指针解析 | gc.c:70, 210-268 | 每轮收集做标记/清扫；自适应阈值 2× 存活（gc.c:3303-3311） |
| 标记：迭代的逐对象工作表（`GCHeader**` 栈） | gc.c:1170-1189 | 每个可达对象单独压栈/出栈 —— 这正是 Green Tea 瞄准的热点循环 |
| 写屏障：512 B 卡表（开放寻址的键集合），每次向老年代对象存储指针时打标记 | gc.c:898-925, 3474-3494 | 在发射代码中无条件执行（LLVMEmitter.penguin:4289-4318） |
| 模式：`EMPEROR_GC_MODE` = `precise`（默认，分代）/ `legacy` / `conservative`；`EMPEROR_GC_NOGEN` 及压力开关 | gc.c:3366-3414 | `greentea` 模式（§12）就嵌在这里 |
| 安全网：每轮 major 收集时的保守式主栈覆盖（gc.c:2836-2844）、最近 512 次分配的隔离区环、`GC_VERIFY` 差分校验机制 | gc.c:3094-3265 | 全部在 v3 中保留 |

堆本身已经通过每类型 ref-map 程序进行**精确式**扫描
（`@<T>_refmap` 常量，编码见 `emperor_types.h:16-43`；写入方
`refmap_append_*` LLVMEmitter.penguin:2737-2844）。v3 改变的是*对象住在哪*
和*标记如何调度* —— 而不是什么被算作引用。

### 2.2 Go 中的 Green Tea（被移植的部分）

- 工作项是 **span**：8 KiB 对齐的区域，其中容纳的对象恰好属于同一个
  size class；该实验覆盖 ≤ 512 B 的对象（Go 1.25/1.26）。
- 每个对象在其所属 span 中有**一个灰色位和一个黑色位**（白色 = 两者皆无）。
  出队时，扫描器对每个位图字计算 `gray & ~black`，把结果拷贝到黑色位，
  并恰好扫描那些对象。子对象在其*自己的* span 中标记灰色位。
- 每个 span 有一个 **`enqueued` 标志**，用于给入队去重（一个待处理的
  span 最多入队一次）。
- **代表对象 + 命中标志**为"恰好只有一个对象被标记"的 span 提供快捷路径：
  那个孤立对象被直接扫描，跳过 span 级簿记。（Go 的数据：在真实堆中，
  很多 span 每轮恰好只有一个存活对象。）
- 队列是 FIFO，因此 span 大致按地址升序被扫描 —— 缓存未命中的减少
  （报告称 L1/L2 未命中减少约 50%）正来源于此。
- 大对象仍走原有的逐对象路径。

## 3. 设计概览

单分代、不移动、stop-the-world（这个世界只有一条 OS 线程；
收集在赋值器线程的 `_emperor_gc_poll` 内部运行，协程栈按构造即处于
停驻状态，可经其帧链枚举）。

```
                    emitted .ll  (UNCHANGED)
   poll @ every call/alloc ──┐   frame-chain link/unlink
   barrier calls (→ no-op)   │   refmaps, metadata globals
                             ▼
 ┌──────────────────────────────────────────────────────────┐
 │  gc.c runtime, greentea mode                             │
 │                                                          │
 │  roots (UNCHANGED set)          gt core (NEW, gc_span.c) │
 │  ┌─────────────────────┐        ┌──────────────────────┐ │
 │  │ frame chains (all    │──mark──▶ GtWorkQueue (FIFO)  │ │
 │  │ stacks incl. corout.)│        │  + rep fast-path     │ │
 │  │ global root registry │        │  + large-object      │ │
 │  │ tracked buffers      │        │    mark stack        │ │
 │  │ meta pins            │        └──────────┬───────────┘ │
 │  │ quarantine ring      │                   │ drain:      │
 │  │ conservative cover   │                   │ gray^black  │
 │  └─────────────────────┘                   │ word-diff   │
 │                                            ▼             │
 │  heap: spans ≤512B ──────────────► sweep (wholesale /     │
 │        malloc blocks >512B ──────►   per-object + large)  │
 └──────────────────────────────────────────────────────────┘
```

无对象头部写入的三色编码：

- **白** = `{gray:0, black:0}` —— 除非被证明可达，（本轮）即不可达，
- **灰** = `{gray:1, black:0}` —— 可达，字段尚未扫描，
- **黑** = `{gray:1, black:1}` —— 可达，字段已扫描。

槽位的颜色位存放在其 **span 位图**中，因此设置/测试一个颜色就是对
64 个相邻对象共享的 64 位字做移位+或运算 —— 标记期间没有逐对象的
头部访问。逐对象的 `GCHeader.marked` 仅在**大对象**（malloc）路径上
继续使用。

**为什么省掉写屏障是安全的：**标记和清扫都在唯一线程上的一次
`gt_collect()` 调用内完成；对象处于灰色期间赋值器从不运行，因此不可能
出现 old→new/白转黑违规。屏障入口点变为空操作（§11）—— 已发射的
`.ll` 无需改动继续工作，v3 也不要求重新发射任何东西。

## 4. 内存布局

### 4.1 superchunk → span

```c
#define GT_SPAN_SIZE     8192              /* one span, 8 KiB-aligned */
#define GT_SPAN_HDR      128               /* sizeof(GtSpan), fixed  */
#define GT_BITMAP_WORDS  4                 /* 4*64 = 256 slots max   */
#define GT_SUPER_SPANS   128               /* 128 * 8 KiB = 1 MiB payload */
#define GT_SUPER_SIZE    (GT_SPAN_SIZE * (GT_SUPER_SPANS + 1))
```

一个 superchunk 通过 `aligned_alloc(GT_SPAN_SIZE, GT_SUPER_SIZE)` 分配
（C11；Win32：`_aligned_malloc`；回退方案：多分配再内部对齐，释放时使用
原始基址）。**最前面的 8 KiB** 存放 `GtSuper` 簿记块，使其余 128 个 span
恰好落在 8 KiB 边界上。

```
superchunk (GT_SUPER_SIZE = 8192*129 bytes, 8 KiB-aligned)
┌────────────┬────────────┬────────────┬────────────┬─────┐
│ GtSuper    │  span #0   │  span #1   │  span #2   │ ... │  (128 spans)
│ 8 KiB      │  8 KiB     │  8 KiB     │  8 KiB     │     │
└────────────┴────────────┴────────────┴────────────┴─────┘
                  │
                  ▼ span #k (one size class)
┌───────────────────────────────┬────────┬────────┬────────┬─────┬────────┐
│ GtSpan header (128 B)         │ slot 0 │ slot 1 │ slot 2 │ ... │ slot N │
│ class,nslots,bitmaps,flags... │ 32..512 B each, 16-byte aligned        │
└───────────────────────────────┴────────┴────────┴────────┴─────┴────────┘
```

### 4.2 尺寸类（size class）

**槽位大小**（24 B `GCHeader` + body，向上取整到所属类）≤ 512 B 的对象
从 span 分配。类的间隔在 512 B 以内沿用 Go 的表格（去掉低于我们
32 B 最小 header+字槽位的类）：

```
slot sizes: 32 48 64 80 96 112 128 144 160 176 192 208 224 240 256
            288 320 352 384 416 448 480 512          (23 classes)
```

| 类（class） | 槽位（slot） | body 容量 | 每 span 槽数（可用 8064 B） |
| --- | --- | --- | --- |
| 0 | 32 | 8 | 252 |
| 1 | 48 | 24 | 168 |
| 2 | 64 | 40 | 126 |
| … | … | … | … |
| 14 | 256 | 232 | 31 |
| … | … | … | … |
| 22 | 512 | 488 | 15 |

更大的请求（槽位 > 512 B）原样走现有的 malloc 块路径
（`_emperor_gc_allocation_list`、排序索引、逐对象标记 —— 即本文档通篇
所说的"large"路径）。字符串经由同一个路由分配（`_emperor_string_alloc`
→ 槽位能装进某个类时走小对象路径）。

### 4.3 地址 → span（O(1)，无索引）

span 按构造即 8 KiB 对齐，因此解析就是掩码 + 校验：

```c
static GtSpan* gt_span_of(const void* p) {
    if (!gt_in_super_range(p)) return NULL;            /* EXACT ranges    */
    GtSpan* s = (GtSpan*)((uintptr_t)p & ~(uintptr_t)(GT_SPAN_SIZE - 1));
    if (s->magic != GT_SPAN_MAGIC) return NULL;       /* false-hit guard   */
    return s;
}
```

`gt_in_super_range` 对排序后的**精确 super 范围**做二分查找（保存在
gc_span.c 中，对应 v2 的 `_gc_super_ranges`）：宽松的 `[heap_lo, heap_hi)`
span 覆盖了各 super 的 mmap 之间未映射的空隙，而 `gt_span_of` 会对掩码出
的基址**解引用** —— 一个落在空隙里的保守式扫描候选会在读取 magic 时
SIGSEGV（M1 测试中发现：ASLR 下约 13% 的 finstorm 运行触发）。属于某个
真实 super 的成员资格使解引用按构造即安全（super 永不释放；位于 super
内的候选掩码出的基址必然落在同一个已映射 super 内）。magic 检查保留用于
super 的簿记块（偏移 0 处是不同的 `GT_SUPER_MAGIC`，会使其检查失败）。
对小对象而言，这取代了排序的 malloc 索引；该索引仅为大对象保留。

### 4.4 对象布局（不变）

每个槽位仍以 24 B 的 `GCHeader`（`{next, marked, is_string, size}`，
gc.c:63-68）开头，随后是 body，其第 0 个字是代码生成盖写的
`EmperorClassMetadata*`（字符串则是 `_emperor_string_metadata*` + 长度 +
数据，`emperor_string.h:27-35`）。由此产生：

- ref-map 遍历、`dispose_mem` finalizer、`is_string` 不透明性以及发射契约
  全部原样可用；
- `next`/`marked` 在 span 中是冗余的（池链和颜色都是 span 级的）——
  属于可接受的开销；无头部槽位是未来工作（§16）。

## 5. 数据结构

新增翻译单元 `EmperorPenguin/std/c/gc_span.c`（span 堆机器：尺寸类、
superchunk 供给、分配、O(1) 解析、清扫/finalizer 遍）；**收集驱动位于
gc.c**（`gc_collect_greentea`，与其兄弟函数并列 —— 它们共享计时统计和
根遍历静态变量）。两个单元之间的共享接口是
`EmperorPenguin/std/include/gc_internal.h`（GCHeader、GT 布局常量、
gt_* API、少数导出的 gc.c 辅助函数）；`gc_span.c` 通过 std/c Makefile 的
SRC 列表加入 libcore_builtin.a。

```c
/* ---- span ---- */
#define GT_F_ENQUEUED    0x01   /* span sits in GtWorkQueue.q            */
#define GT_F_REP_PENDING 0x02   /* rep_slot grayed, not yet scanned      */
#define GT_F_REP_HIT     0x04   /* ≥2 distinct objects grayed → real scan */

typedef struct GtSpan {
    uint32_t magic;              /* GT_SPAN_MAGIC, guards gt_span_of()   */
    uint8_t  size_class;         /* index into gt_classes[]              */
    uint8_t  flags;              /* GT_F_*                               */
    uint16_t nslots;             /* slots per span for this class        */
    uint16_t free_index;         /* next slot probe hint (alloc)         */
    uint16_t live_count;         /* allocated (not yet swept-dead) slots */
    uint16_t rep_slot;           /* representative slot index            */
    uint64_t alloc_bits[GT_BITMAP_WORDS];  /* slot in use                */
    uint64_t gray_bits[GT_BITMAP_WORDS];   /* reachable, unscanned       */
    uint64_t black_bits[GT_BITMAP_WORDS];  /* reachable, scanned         */
    struct GtSpan* next;         /* pool list link (partial/full/empty)  */
    struct GtSuper* super;       /* owning superchunk                    */
} GtSpan;                        /* == GT_SPAN_HDR (128 B), static_assert */

/* ---- size class ---- */
typedef struct GtSizeClass {
    uint16_t slot_size;          /* 32..512, multiples of 16             */
    uint16_t nslots;             /* floor(8064 / slot_size)              */
} GtSizeClass;                   /* gt_classes[23], const                */

/* ---- superchunk bookkeeping (first 8 KiB of a superchunk) ---- */
typedef struct GtSuper {
    struct GtSuper* next;        /* _gt_supers list (never freed by default) */
    void*  raw;                  /* aligned_alloc base (free target)     */
    size_t size;
    uint32_t nspans;             /* GT_SUPER_SPANS                       */
    uint32_t nempty;             /* spans still on the empty pool        */
} GtSuper;

/* ---- heap ---- */
typedef struct GtHeap {
    GtSpan* current[GT_NCLASS];  /* active allocation span per class     */
    GtSpan* partial[GT_NCLASS];  /* swept spans with free slots          */
    GtSpan* full[GT_NCLASS];     /* swept spans, no free slots           */
    GtSpan* empty;               /* class-agnostic; reclassed on demand  */
    GtSuper* supers;
    /* stats */
    size_t heap_bytes;           /* sum of allocated slot bytes + large  */
    size_t live_bytes;           /* after last sweep                     */
    /* goal_bytes moved to gc.c at v3.1: ONE unified dual-heap goal    */
    uint32_t n_wholesale;        /* spans recycled whole this cycle      */
    uint32_t n_rep_scans;        /* representative fast-path hits        */
    int     mark_overflow;       /* queue/stack alloc failure → retain   */
} GtHeap;

/* ---- mark work queue (FIFO ring of spans) ---- */
typedef struct GtWorkQueue {
    GtSpan** q; size_t cap, head, count;
    /* plus the rep fast-path list and the large-object stack: */
    GtSpan*  rep_list;           /* singly-linked via ->next              */
    GCHeader** large;            /* per-object stack, malloc'd blocks     */
    size_t large_cap, large_top;
} GtWorkQueue;
```

注意事项：

- `GtSpan.next` 是复用的：停驻时作为池链，作为待处理代表时是
  `rep_list` 链（一个 span 绝不会同时处于两个结构中 —— 由标志位指明
  当前是哪个）。
- 空池是**与类无关的**：被清空的 span 会被重新 `memset` 并按需重新
  归类，因此类之间不会搁浅内存。
- 所有结构都是进程级全局单例；任何地方都不加锁（单赋值器，与 v2 相同的
  不变式 —— 今天的 gc.c 里就没有锁）。

## 6. 分配

### 6.1 路由

`_emperor_alloc_impl(int size)`（在每个 `new`/`BOX` 处发射，
LLVMEmitter.penguin:4934/5123/5489/5507；字符串经 `_emperor_string_alloc`）：

```
slot = round8(24 + size)
if (greentea mode && slot <= 512)  return gt_alloc_small(size, slot);
else                               return <existing malloc-block path>;
```

### 6.2 快速路径

```c
static void* gt_alloc_small(int size, int slot) {
    unsigned cls = gt_class_of[slot];              /* O(1) lookup table   */
    GtSpan* s = _gt.current[cls];
    if (!s && !(s = gt_refill_current(cls)))       /* slow path, §6.3     */
        return NULL;
    for (unsigned w = s->free_index >> 6; w < GT_BITMAP_WORDS; w++) {
        uint64_t free = ~s->alloc_bits[w];
        if (!free) continue;
        unsigned idx = (w << 6) + ctz64(free);
        s->alloc_bits[w] |= 1ull << (idx & 63);
        s->free_index = idx + 1;
        s->live_count++;
        _gt.heap_bytes += gt_classes[cls].slot_size;
        GCHeader* h = gt_slot_header(s, idx);
        h->next = NULL; h->marked = 0; h->is_string = 0; h->size = size;
        memset(h + 1, 0, size);                    /* zeroing stays       */
        if (_gt.heap_bytes > _gt.goal_bytes) _gt_want_collect = 1;
        return h + 1;
    }
    /* current span is full: retire and retry once */
    gt_pool_push(&_gt.full[cls], s);
    _gt.current[cls] = NULL;
    return gt_alloc_small(size, slot);
}
```

成本：从提示位开始的一次位图扫描 + 一次 `memset`。与 v2 nursery 的
bump 分配同阶（后者同样要 memset），只是省去了逐对象的链表追加。

### 6.3 慢速路径（补充/refill）

```
gt_refill_current(cls):
    1. partial[cls] non-empty            → pop, make current.
    2. else empty pool non-empty         → memset header, set class/nslots,
                                            make current (reclass).
    3. else cut spans from a fresh superchunk (gt_super_acquire);
       push the remaining 127 onto the empty pool.
    4. if superchunk alloc fails:
         gt_collect(emergency=1);        /* free memory, then retry      */
         retry steps 1-3 once; on failure return NULL (OOM abort path,
         same fatal reporting as v2).
```

被清扫释放的槽位只有在该次清扫**之后**才可复用（分配路径从不清除
alloc 位；只有清扫会清）。收集周期中途不存在复用 —— 与 v2 的生命周期
语义完全一致，也正因如此才可能有整体死亡的 span。

## 7. 标记

### 7.1 入口

所有根来源（§10）都汇入同一个访问器 `gt_mark(void* p)`，其中 `p`
是来自精确式根的精确用户指针，或来自保守式覆盖的任意候选指针：

```c
static void gt_mark(void* p) {
    GtSpan* s = gt_span_of(p);
    if (s) {
        void* base = gt_slot_user_base(s, p);   /* interior → owner slot  */
        if (base) gt_gray_slot(s, gt_slot_index(s, base));
        return;
    }
    GCHeader* h = gc_resolve_block(p);          /* large path, gc.c:254   */
    if (h) gt_mark_large(h);
}
```

`gt_slot_user_base` 以 O(1) 把内部指针解析到其所属槽位：
`idx = (p − span_payload)/slot_size`，校验 alloc 位，并确认 `p` 位于
`[slot_user_base, slot_user_base + h->size)` 之内（与 v2 的
`gc_owner_in_chunk` 相同的包含规则，gc.c:1240-1269 —— +16 处的 enum
载荷和嵌套的值类按设计就是内部指针）。这用一次地址除法取代了 chunk 遍历。

### 7.2 槽位置灰（去重 + 代表簿记）

```c
static void gt_gray_slot(GtSpan* s, unsigned idx) {
    uint64_t bit = 1ull << (idx & 63);
    unsigned w   = idx >> 6;
    if (s->gray_bits[w] & bit) return;          /* already gray           */
    s->gray_bits[w] |= bit;
    if (s->flags & GT_F_ENQUEUED) return;       /* queued: drain will see */
    if (s->flags & GT_F_REP_PENDING) {
        if (s->rep_slot == idx) return;
        s->flags |= GT_F_REP_HIT;               /* 2nd distinct object    */
        gt_enqueue(s);                          /* real scan required     */
        return;
    }
    s->rep_slot  = idx;                         /* candidate lone object  */
    s->flags    = (s->flags & ~GT_F_REP_HIT) | GT_F_REP_PENDING;
    gt_rep_push(s);                             /* NOT enqueued           */
}
```

恰好只有一个灰色对象的 span 永远不进入工作队列：它停留在**代表数组**
上，其唯一对象在排空（drain）时被直接扫描（`n_rep_scans` 统计命中
次数）。出现第二个不同对象时翻转 `REP_HIT`，并将其入队做完整位图扫描。

**与原始草图的实现偏差（M2）：**代表簿记是一个可增长数组（由头部索引
消费），而不是规范中通过 `GtSpan.next` 链接的 `rep_list` —— 标记期间
span 的 `next` 是它的池链（current/partial/full），无法兼作标记结构链接。
当 span 已离开上述两种结构后某候选再次变灰时，重新挂起之前要先清除
过期的 `REP_HIT`（否则排空会把它当作已晋升而跳过）。标记的叶子操作不做
"先解析再置灰"：`gt_mark_candidate(p)` 把 span 成员资格测试、属主解析和
置灰合并为一次范围查找（成对形式对每个引用字要做两次 super 范围二分
查找 —— 在 M2 首个版本中占 deepsurvive 标记的 52%）。

### 7.3 排空循环

```c
static void gt_drain(void) {
    for (;;) {
        GCHeader* lh;
        if (gt_large_pop(&lh)) { gt_scan_object(lh + 1); continue; }

        GtSpan* s = gt_dequeue();               /* clears GT_F_ENQUEUED   */
        if (s) { gt_scan_span(s); continue; }

        s = gt_rep_list_pop();
        if (!s) break;                          /* all work done          */
        assert(!(s->flags & GT_F_REP_HIT));     /* HIT path enqueued it   */
        s->flags &= ~GT_F_REP_PENDING;
        unsigned idx = s->rep_slot;
        s->black_bits[idx >> 6] |= 1ull << (idx & 63);
        _gt.n_rep_scans++;
        gt_scan_object(gt_slot_user(s, idx));   /* single-object fast path */
    }
}

static void gt_scan_span(GtSpan* s) {           /* full bitmap scan       */
    s->flags &= ~GT_F_REP_PENDING;              /* (if HIT promoted it)   */
    for (;;) {
        int progressed = 0;
        for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
            uint64_t fresh = s->gray_bits[w] & ~s->black_bits[w];
            if (!fresh) continue;
            s->black_bits[w] |= fresh;          /* blacken batch          */
            while (fresh) {
                unsigned idx = (w << 6) + ctz64(fresh);
                fresh &= fresh - 1;
                gt_scan_object(gt_slot_user(s, idx));
            }
            progressed = 1;
        }
        if (!progressed) break;                 /* gray == black for s    */
    }
}
```

`gt_scan_object(user)` 是现有的精确式 body 遍历，从 v2 的标记路径中
析出：字符串（`is_string`）立即返回；带 ref-map 的对象调用
`gc_refmap_walk(map, MARK mode)`，其叶子动作现在是 `gt_mark(child)` 而非
`_emperor_gc_mark_object`；无映射对象（外来元数据）回退到保守式 body
字扫描（gc.c:1761-1782）。大对象使用逐对象的 `marked` 头部位和
`large` 栈（旧工作表，现在只用于 malloc 分配的块）。

**终止性。**span 只有在其局部 `gray == black` 时才会离开队列（内层
`progressed` 循环）；span 离开 `rep_list` 只有两种方式：经由 HIT
（会重新入队）或扫描完其代表对象（将其变黑）。由于赋值器已停止，
排空结构清空之后不可能再出现新的灰色位 —— 收集以全局 `gray == black`
结束，在 `GC_VERIFY` 下有断言。

**失败。**如果标记中途队列/栈增长所需的 `realloc` 失败，标记就是不
完整的：v3 保留 v2 的全保留策略（`_emperor_gc_mark_failed`，
gc.c:1174-1177）—— 跳过清扫，视所有对象为存活，下一轮重试。

### 7.4 为什么它比逐对象工作表更快

v2 循环要为**每个可达对象**支付一次工作表压栈/出栈，并按发现顺序
（指针追逐的局部性）在对象之间跳跃。v3 循环只为**每个含 ≥2 个被标记
对象的 span** 支付一次压栈/出栈，每次按 64 个槽位步进遍历
`gray & ~black`，并且 —— 由于 span 以其首次被触及的大致顺序从 FIFO 中
出来 —— 以近乎线性的地址顺序触及对象内存。整体死亡的 span 根本不会被
标记器触碰。这正是 Go 报告的 10–40% 背后的同一机制（他们的数字包含
并行扫描；我们单线程的预期是低端，基准门槛定为 ≥15%，见 §14）。

## 8. 清扫

标记成功后，`black` 即存活。清扫对每个 span 只走一遍：

```c
static void gt_sweep_span(GtSpan* s) {
    /* black == live at this point; gray bits are all blackened copies   */
    unsigned live = 0;
    for (unsigned w = 0; w < GT_BITMAP_WORDS; w++) {
        uint64_t alloc  = s->alloc_bits[w];
        uint64_t dead   = alloc & ~s->black_bits[w];
        if (dead) {
            uint64_t d = dead;
            while (d) {                          /* finalizers still run  */
                unsigned idx = (w << 6) + ctz64(d);
                d &= d - 1;
                gt_run_finalizer(gt_slot_header(s, idx));  /* §8.1        */
            }
            s->alloc_bits[w] = alloc & s->black_bits[w];
        }
        s->gray_bits[w] = 0;
        s->black_bits[w] = 0;
        live += popcount64(s->alloc_bits[w]);
    }
    s->live_count = live;
    s->free_index = 0;
    s->flags &= ~(GT_F_ENQUEUED | GT_F_REP_PENDING | GT_F_REP_HIT);
    _gt.live_bytes += live * gt_classes[s->size_class].slot_size;
    gt_repool(s, live == 0 ? EMPTY : live < s->nslots ? PARTIAL : FULL);
}
```

- **整体回收**：`live == 0` 的 span 完全跳过逐槽位释放处理 —— 在
  finalizer 探测之后，它们带着一次头部 `memset` 进入与类无关的空池。
  幼年夭折（infant-mortality）型负载（即 v2 nursery 服务的那些）大多
  产生整体死亡的 span，因此回收成本从 O(对象数) 降到 O(span 数)。
- **finalizer 探测**（§8.1）是死亡 span 支付的唯一逐对象工作。
- 大对象清扫复用 v2 的 `_emperor_gc_sweep`（gc.c:1852-1912），去掉了
  卡表压缩步骤。
- 默认情况下 super 在进程生命周期内保留（v2 行为，gc.c:2264-2305 注释）；
  超过缓存阈值后释放完全为空的 super 是一个策略开关（§12），默认关闭。

### 8.1 死亡槽位上的 finalizer

`dispose_mem`（`IMemoryDispose`）对裸缓冲区属主（vector/hashmap/array）
至关重要。分配时运行时还不知道调用者的类是否有析构器（body 第 0 字是在
`_emperor_alloc_impl` 返回*之后*才盖写的），因此无法在分配时维护
`finalizer_count`。取而代之，清扫对每个**死亡**槽位探测一次：读取
body 第 0 字 → `metadata->destructor`；若非 NULL，调用 `dispose_mem(obj)`
（规则与 v2 相同，gc.c:1835-1850）。探测对每个死亡对象只是一次顺序读取
（对预取友好）；`is_string` 槽位跳过。优化推迟到未来工作：带编译期已知
"无 finalizer"位的增量式 `_emperor_alloc_impl(size, flags)`（§16）。

## 9. 收集周期与触发

### 9.1 `gt_collect`

```
gt_collect(emergency):
    _emperor_gc_collecting = 1
    stats reset (n_wholesale, n_rep_scans, live_bytes = 0)
    walk all roots → gt_mark            /* §10: registry, frame chains of
                                           every stack incl. parked coroutines
                                           (scheduler frame-head enumeration,
                                           scheduler.c:180), tracked buffers,
                                           meta pins, quarantine ring,
                                           conservative stack cover          */
    gt_drain()
    if (_gt.mark_overflow) { retain everything; goto done; }
    GC_VERIFY: assert gray==black per span; black ⊆ alloc;
               optional differential vs conservative whole-heap scan
    gt_sweep_all_spans()                /* §8                                */
    sweep_large()                       /* existing malloc-block sweep      */
    gc_update_unified_goal(freed)       /* §9.2 — ONE goal over both heaps  */
done:
    _emperor_gc_collecting = 0
```

协程安全：与 v2 的 major 收集一致 —— 收集只在运行线程的安全点轮询内部
运行；所有其他栈都处于停驻状态，其帧链完整且可枚举，其扫描区域收窄到
`[parked_sp, top]`（`_emperor_gc_scan_set_live`，scheduler.c:319-355）。

### 9.2 触发与堆目标 —— 统一的双堆预算

> **勘误（v3.1）**：本节原文规定的是两个各自独立的本地预算 ——
> gc_span.c 中的 span 目标（`max(4 MiB, factor × span-live)`）和
> malloc 侧的自适应阈值（`2 × malloc-live`，gc.c）—— 二者独立进行
> 垃圾充裕式放大。在自举上的实测表明这是 v3.0 的头号成本缺陷：每一侧
> 只按*自己的*存活集定尺寸，但每轮收集都要以 O(总存活) 标记*两个*堆，
> 于是较小的预算决定了节奏，而收集却支付大侧的成本（malloc-live 约
> 40 MiB 对 span-live 约 250 MiB → malloc 阈值每约 87 MiB 触发一次
> 收集，59 次 full，每次都要遍历合计 290 MiB 的存活集；同样的负载
> v2 只做了 10 次 major）。v3.1 用一个覆盖*合并*堆的统一目标取代了
> 这两者。

- `_emperor_gc_poll()`（在每次调用和分配前发射）检查
  `_emperor_gc_want_collect` 并运行 greentea 周期。这是唯一的常规
  触发器；轮询点契约保持不变。
- **触发（任一分配侧）**：`_emperor_gc_total_allocated +
  gt_heap_bytes() >= unified_goal` → `_emperor_gc_want_collect = 1`。
  `gt_alloc_small` 和 `_emperor_gc_alloc` 中的 malloc 路径都调用
  `gc_unified_goal_reached()`（gc.c；conservative 模式保留旧的仅 malloc
  阈值 —— 它没有 span 堆，退化为旧行为）。
- **更新（每周期一次，清扫后）**：gc.c 中的
  `gc_update_unified_goal(malloc_freed)` 计算
  `live = malloc-live + gt_live_bytes()` 并设置
  - `goal = max(GC_GOAL_MIN_HEAP 4 MiB, EMPEROR_GC_GOAL_FACTOR × live) +
    EMPEROR_GC_YOUNG (128 MiB)` —— 相当于把 v2 的经济学放到一个堆上：
    比例基项是 v2 老年代的 2×-live 阈值，加法项的年轻代预算是 nursery
    固定周转量的摊销（也就是 M3 垃圾充裕式翻倍曾经扮演的角色；作为
    "带上限的翻倍"来度量，它在存活攀升期间要么从不触发 —— 纯
    factor×live 目标会把垃圾比例钉死在 1/(1+factor) —— 要么在
    "先攀升后周转"的曲线上把峰值堆推到约 4× live，因此这份松弛被直接
    加进基项）；
  - 存活占优时（`freed < live/4`）：`goal ×= 4`，上限 4 TiB —— v2 的
    老年代升级策略，为 LSP 重编译形态保留（几乎释放不出什么，而每轮
    收集却要付出 O(存活块) 的成本）；
  - 永不收缩（与 v2 一致）。
  峰值堆被约束在 `(1+factor) × live + young`（自举实测：目标 754 MiB +
  约 475 MiB 固定非 GC 开销 = 1231 MiB maxrss）。
- `gt_update_goal`/`gt_set_goal_params` 已删除；开关解析
  （`EMPEROR_GC_GOAL_FACTOR`/`EMPEROR_GC_MIN_HEAP`）喂给 gc.c 中的
  `gc_set_goal_params`，`gc_gt_stats(1)`（目标字节数）经
  `gc_unified_goal_value()` 报告统一目标。
- **紧急**：`gt_refill_current` 失败（无空闲槽位、空池耗尽、superchunk
  malloc 失败）→ 内联紧急收集，重试一次，然后 OOM（v2 的致命路径）；
  malloc 路径仍在达到目标 8 倍时内联收集（重度使用 C 且久无轮询的时段）。
- `EMPEROR_GC_STRESS_EVERY=N`（gc.c）继续有效：每 N 次分配收集一次 ——
  §14 的首要正确性重锤。

## 10. 根与安全网（清单保持不变）

| 根来源 | 机制 | v3 变化 |
| --- | --- | --- |
| 栈帧（所有栈） | `EmperorGcFrame` 链在进入时挂到 `_emperor_gc_frame_head`、在 ret 时摘除（LLVMEmitter.penguin:1642-1667）；裸槽位 + 经 ref-map 的 struct 居所 | 无 —— 叶子动作改为调用 `gt_mark` |
| 全局变量 | `_emperor_gc_add_root(void**)` 注册表；值类全局变量作为扫描区域（LLVMEmitter.penguin:1695-1703） | 无 |
| 容器缓冲区 | `#__track_buffer` 类型化区域（Vector/Array/HashMap，`vector.penguin:26-96`） | 无 |
| Meta/JIT | `_emperor_gc_pin_object` 固化的地址（gc.c:1085-1109）；JIT 单元经 `-rdynamic` 通过同一个 `_emperor_alloc_impl` 分配 | 无 |
| 隔离区环 | 每周期固定最近 512 次分配（gc.c:638-650）—— 覆盖在途的 SSA 临时对象 | 初期保留；退役以长期 `GC_VERIFY` 证据为前提（§16） |
| 保守式栈覆盖 | 主栈 + 协程栈的字扫描（gc.c:1821-1831, 2836-2844），寄存器经 `setjmp` 溢出保存（gc.c:2984-2986） | 作为安全网保留。在不移动的收集器中，保守式命中只是一个额外的标记 —— **不需要任何固定机制**，这正是 v3 得以删除 v2 固定/降级/墓碑代码的原因 |

`GC_VERIFY` 差分机制（gc.c:3094-3265）扩充了 §9.1 的两个 span 不变式；
现有的保守式对精确式差分继续验证根的完备性。

## 11. 写屏障与 ABI

- **M4c（已完成）**：发射器不再发射屏障调用（LLVMEmitter.penguin 中的
  字段存储与回退存储点）；运行时保留空操作的符号体，因此先前发射的
  `.ll`（以及 `.penguin-lib` 消费方）仍可链接和运行。对称地，*不带*
  屏障发射的代码只有配 v3 运行时才正确 —— 由 ABI 标签守护。
- 运行时 ABI 标签（混合 dynlib 构建时比较）：`emperor-rt-gc2-3d`
  → **`emperor-rt-gc3-gt1`**（在 M4c 提升）。
- JIT（`penguin_jit.cpp`，ORC `GetForCurrentProcess`）与 dynlib 消费方
  完全像今天一样从宿主 exe 绑定
  `_emperor_alloc_impl`/`_emperor_gc_poll`/帧头；本设计不需要任何新导出
  符号（所有 `gt_*` 函数都是内部的）。
- JIT（`penguin_jit.cpp`，ORC `GetForCurrentProcess`）与 dynlib 消费方
  完全像今天一样从宿主 exe 绑定
  `_emperor_alloc_impl`/`_emperor_gc_poll`/帧头；本设计不需要任何新导出
  符号（所有 `gt_*` 函数都是内部的）。

## 12. 模式与开关

| 开关 | 取值 / 默认 | 含义 |
| --- | --- | --- |
| `EMPEROR_GC_MODE` | `greentea` —— **默认**（未设置/未知时）/ `conservative`（紧急回退，保留）。`precise`/`legacy` 已随 v2 分代机器在 M4b 删除 —— 它们会告警并回退到 greentea（如需二分定位请用更旧的代码树） | 收集器选择（gc.c 初始化接线） |
| `EMPEROR_GC_GOAL_FACTOR` | `2` | 统一目标对*合并的* malloc+span 存活字节数的乘数（v3.1，§9.2） |
| `EMPEROR_GC_MIN_HEAP` | `4194304` | 统一目标的比例下限（字节） |
| `EMPEROR_GC_SPAN_CACHE` | 无限 | 释放 super 之前空池上保留的最大 span 数（0 = v2 的永久保留行为，默认）—— 尚未实现；super 目前在进程生命周期内保留 |
| `EMPEROR_GC_YOUNG` | `13421772`（128 MiB） | 统一目标的加法项周转松弛（在 factor×live 基项之上再加一份 nursery 预算，§9.2）；内存紧张的主机可以调低 |
| `EMPEROR_GC_STRESS_EVERY` | 关闭 | 每 N 次分配收集一次（既有功能，gc.c:3407） |
| `EMPEROR_GC_DISABLE` / `GC_VERIFY` / `EMPEROR_GC_NO_STACK_COVER` | 既有 | 语义不变（gc.c:74-78, 3364, 3395-3402） |

`gc_info()` / `_emperor_gc_info_split`（gc.c:3427-3447）新增增量字段
（M3）：`_emperor_gc_gt_stats(which)`（C 侧）以 penguin 内建函数
`gc_gt_stats(which: i64) -> i64` 暴露（`__builtin` extern，与 gc_info
一同注册在 SemanticModel 中；仅在 greentea 模式下有意义的计数器 ——
模式之外为 0）：0 存活槽位字节数，1 目标字节数，2/3/4
full/partial/empty 的 span 数，5 整体回收的 span 数（累计），6 代表
快捷路径扫描（累计），7 已完成的周期数，8 superchunk 数。
`Tests/GcTest/GreenTeaSpanStats.md` 端到端断言这些计数器。

## 13. 关键函数参考

新增（全部位于 `gc_span.c`，除注明处外为内部链接）：

| 函数 | 用途 |
| --- | --- |
| `gt_class_of[slot]` / `gt_classes[23]` | 尺寸类表 + O(1) 槽位→类查询（初始化时构建一次） |
| `gt_span_of(p)` / `gt_in_super_range(p)` | O(1) 掩码 + 精确范围 + magic 校验（§4.3） |
| `gt_slot_user_base / gt_slot_index / gt_slot_header` | 内部指针 → 所属槽位（§7.1） |
| `gt_alloc_small(size, slot)` | 快速路径（§6.2）；由 `_emperor_alloc_impl` 调用 |
| `gt_refill_current(cls)` | 慢速路径（§6.3）；负责紧急收集 |
| `gt_super_acquire / gt_span_reclass` | superchunk 切分；空 span 重新归类 |
| `gt_mark(p)` | 根访问器入口（§7.1）；接入各根遍历 |
| `gt_gray_slot / gt_enqueue / gt_rep_list_push/remove` | 颜色簿记（§7.2） |
| `gt_scan_span / gt_scan_object / gt_drain` | 标记循环（§7.3） |
| `gt_sweep_span / gt_sweep_all / gt_run_finalizer` | 清扫 + finalizer（§8） |
| `gt_collect(emergency)` | 周期驱动（§9.1）—— 在 gc.c 中实现为 `gc_collect_greentea`（共享统计/根遍历）；由 `_emperor_gc_poll` 和 `gc_collect()` 调用 |

**M1 清扫分相**（承重的顺序保证；`gc.c`）：v2 的 `_emperor_gc_sweep`
被拆成 `gc_sweep_pass1`（摘链 + finalizer）和 `gc_sweep_pass2`（索引
压缩 + 释放）。greentea 驱动的顺序为：malloc pass1 →
`gt_sweep_finalize`（span 死亡槽位的 finalizer；死亡的 malloc body 保持
原样、不释放）→ malloc pass2 → `gt_sweep_reclaim`。无论哪个堆，每个
finalizer 看到的每个死亡对象都是完好的，而 finalizer 执行期间的分配
（malloc 路径经收集闩锁挡下）绝不会被本轮的摘链遍历访问 —— 不需要
逐对象 marked 位之类的技巧。

在 `gc.c` 中修改：

| 位置 | 变更 |
| --- | --- |
| `_emperor_alloc_impl`（约 gc.c:3540-3611 区域） | greentea 模式下把小请求路由到 `gt_alloc_small` |
| `_emperor_gc_poll`（gc.c:3581-3611） | 触发检查 → `gt_collect` |
| 模式初始化（gc.c:3366-3381） | 解析 `greentea`，在 `_emperor_gc_init` 处选择堆形态 |
| 根遍历（帧链 gc.c:1138-1163、注册表、区域、覆盖） | 叶子动作可插拔：`_emperor_gc_mark_object` ↔ `gt_mark` |
| `_emperor_gc_write_barrier(_map)`（gc.c:3474-3494） | greentea 模式下为空操作（§11） |
| `gc_info` 拆分结构体（gc.c:3427-3447） | 增量式 GT 统计字段 |

在 M4b 删除（v2 机器，约 1550 行）：`gc_minor` 与整套疏散机器
（晋升/墓碑/固定/降级、疏散工作表、疏散/固定帧链遍历）、nursery
（YoungChunk/YoungSuper、chunk 供给、chunk 索引、bump 分配、
maybe_nursery/super 范围）、卡表 + 脏卡扫描 + 屏障老年代目标逻辑（屏障
*符号*保留为 ABI 空操作）、pending-old 成员哈希、`gc_collect_generational`、
救援上报上下文、ref-map 遍历的 EVACUATE 模式，以及
`precise`/`legacy`/`EMPEROR_GC_NOGEN` / `EMPEROR_GC_RS_FALLBACK` /
`EMPEROR_GC_STACK_COVER` / `EMPEROR_GC_REGION_PINS` 等开关（其机制已
不复存在）。`conservative`（内联收集的紧急回退）保留。
`EMPEROR_GC_YOUNG` 以垃圾充裕目标上限的身份存续（§9.2）。

## 14. 测试与基准

- **正确性重锤**：`EmperorPenguin/std/c/gc_torture.c`（746 行，可独立
  编译）扩展了 span 堆负载：链表周转（幼年夭折）、树重建、共享 DAG、
  容器周转（Vector/HashMap）、带存活帧的深递归、finalizer 风暴、
  字符串周转、横跨 512 B 边界的混合大小，以及上述所有负载的
  `EMPEROR_GC_STRESS_EVERY=1` 变体。一个 `greentea_section` 断言 M1 的
  交付面：span/malloc 路径图的完整性、精确的 finalizer 计数，以及精确的
  整体回收差值（greentea 下 `_emperor_gc_alloc_charge` 按 size class
  槽位计费，使精确差值断言跨模式可移植）。
  **ASan 测试纪律**：使用 `-fsanitize=address` 时，局部变量可能住在
  ASan 的*伪栈*（堆上分配的帧）中 —— 此时 `_emperor_gc_init(&local)`
  括住的是一段非栈范围，保守式覆盖什么都找不到
  （`ASAN_OPTIONS=detect_stack_use_after_return=0` 可恢复）。
  测试必须通过 `_emperor_gc_add_root`/静态变量给句柄立根（发射出的
  main 传入 `llvm.frameaddress(0)`，即帧顶，因此生产环境不受影响 ——
  与 v2 分代小节已有的纪律相同）。
- **e2e**：10 个 `Tests/GcTest/*.md` 用例以 `EMPEROR_GC_MODE=greentea`
  运行（该套件已有环境变量驱动的机制）；新增
  `Tests/GcTest/GreenTea*.md` 用例断言 `gc_info()` 的整体回收计数。
  每个里程碑都保证完整 `make test` 矩阵 + `make unittest` 全绿。
- **自举**：`.ll` 发射在 M4 之前保持不变，因此 pass2–pass5 的 md5
  收敛不受 M1–M3 影响；M4（移除屏障发射）需要一整轮 `make bootstrap`
  重新收敛。
- **M4 默认切换门槛发现了两个选择性加入（opt-in）门槛发现不了的缺陷**
  （均已修复；均已固化为哨兵用例）：
  1. **环形队列回绕+扩容**（gc_span.c）：在环形队列处于回绕状态时扩容
     FIFO，会把按旧模数写入的前缀孤立掉 —— 随后 `gt_dequeue` 会遍历到
     未初始化的槽位（pass3 自编译 SIGSEGV；gc_torture 的 greentea 小节里
     有确定性的复现形态：PAIRED 节点的边指向一对节点的两个成员，保证
     每个目标 span 至少被置灰 2 次，于是排空的 BFS 前沿跑赢了消费速度；
     稀疏/跨组的随机边不会触发它 —— 代表路径占主导）。修复：扩容时
     先解除回绕（把回绕的前缀 memmove 到新尾部）。
  2. **enum 全局变量从未立根**（LLVMEmitter emit_main）：enum 类型
     全局变量的内联 {meta, tag, payload} 结构体的 payload 中可以持有
     引用，但注册循环只覆盖了值类 struct 与 ref/string 槽位
     （LambdaCaptureGcOptionPayload —— `Option<fun>` 中的闭包每轮都被
     释放；v2 nursery 曾用晋升把它掩盖了）。修复：把 enum 全局变量
     注册为保守式扫描区域。
- **基准测试（默认切换的门槛）**：`make gc-bench`（M0 新增目标 ——
  `EmperorPenguin/std/c/gc_bench.sh`）驱动 `gc_torture bench <workload>`，
  配合 `GC_PROFILE` 输出分相计时（标记 / 清扫 / 总暂停）。门槛：指针
  密集与容器周转负载上标记相 CPU 降低 ≥15%；深存活负载上回归 ≤5%
  （移除 nursery 的已知代价），对照同一二进制上的 v2 `precise` 度量。

**M0 基线（v2 `precise`，记录于 2026-09；开发主机 linux x86_64，
clang release 构建，一次 `make gc-bench` 运行的中位数 —— M3 门槛比较的
是同次运行内的相对差值）：**

| workload | majors | mark_ms | sweep_ms | gc_total_ms | allocs | live peak |
| --- | --- | --- | --- | --- | --- | --- |
| churn | 16 | 39.0 | 0.001 | 39.0 | 5.00 M | ~0 MiB |
| ptrdense | 16 | 200.6 | 0.006 | 309.8 | 2.04 M | 3.2 MiB |
| container | 12 | 12.3 | 0.457 | 12.7 | 1.50 M | 0.1 MiB |
| deepsurvive | 160 | 8514.3 | 9.7 | 9084.2 | 3.86 M | 55.0 MiB |
| finstorm | 60 | 4.8 | 0.001 | 4.8 | 1.20 M | ~0 MiB |
| mixed512 | 8 | 58.5 | 0.023 | 58.6 | 1.00 M | ~0 MiB |

负载形态（全部确定性，`gc_torture.c::bench_*`）：`churn` = 4M 次
幼年夭折的小对象+短命字符串分配，每 262143 次迭代显式收集一次；
`ptrdense` = 30k 持久节点链表 + 重链周转，2M 次迭代，每 131071 次收集
一次；`container` = 32 个类型化缓冲区 × 64 槽位，1.5M 次元素重写，每
131071 次收集一次；`deepsurvive` = 森林经 160 次显式收集长到 655k
存活节点（约 55 MiB）+ 批间周转；`finstorm` = 60 轮 × 每轮 20k 个带
析构器标记的对象被杀死；`mixed512` = 1M 次分配，body 大小在 200..600 B
均匀分布、横跨 span 截断线，每 131071 次收集一次。

**M2 数字（greentea span 批量标记 + 位图清扫，同一主机，同日 v2 对比）：**

| workload | v2 mark_ms | M2 mark_ms | mark Δ | v2 total_ms | M2 total_ms |
| --- | --- | --- | --- | --- | --- |
| churn | 39.0 | 0.8 | −98% | 39.0 | 18.2 |
| ptrdense | 200.6 | 37.6 | −81% | 309.8 | 42.4 |
| container | 12.3 | 1.9 | −85% | 12.7 | 5.0 |
| deepsurvive | 8514.3 | 1742.4 | −80% | 9084.2 | 1842.1 |
| finstorm | 4.8 | 0.1 | −98% | 4.8 | 4.5 |
| mixed512 | 58.5 | 40.6 | −31% | 58.6 | 51.5 |

**M3 数字（目标启发式 + 垃圾充裕摊销 + gc_gt_stats）：**

| workload | v2 majors | M3 majors | v2 mark_ms | M3 mark_ms | mark Δ | v2 total_ms | M3 total_ms |
| --- | --- | --- | --- | --- | --- | --- | --- |
| churn | 16 | 18 | 39.0 | 0.08 | −99.8% | 39.0 | 22.9 |
| ptrdense | 16 | 46 | 200.6 | 29.4 | −85% | 309.8 | 33.8 |
| container | 12 | 13 | 12.3 | 0.6 | −95% | 12.7 | 3.4 |
| deepsurvive | 160 | 160 | 8514.3 | 1800.7 | −79% | 9084.2 | 1895.9 |
| finstorm | 60 | 60 | 4.8 | 0.05 | −99% | 4.8 | 2.6 |
| mixed512 | 8 | 12 | 58.5 | 8.0 | −86% | 58.6 | 39.4 |

M3 门槛（指针密集/容器上标记 −15%；深存活总回归 ≤5%）被大幅超出 ——
包括深存活在内，它比 v2 快 4.8×（nursery 复制带来的红利被 span 整体
回收 + 批量标记器超额弥补）。M3 调优以 ≤128 MiB RSS 下的 nursery 持平
摊销，取代了平坦目标在周转形态上的过度收集（mixed512 168 → 12 轮，
churn 77 → 18）。

**v3.1 数字（统一双堆目标，§9.2 —— 同一主机，同一方法）：**

| workload | v2 majors | M3 majors | v3.1 majors | M3 total_ms | v3.1 total_ms |
| --- | --- | --- | --- | --- | --- |
| churn | 16 | 18 | 16 | 22.9 | 39.7 |
| ptrdense | 16 | 46 | 16 | 33.8 | 15.6 |
| container | 12 | 13 | 12 | 3.4 | 3.4 |
| deepsurvive | 160 | 160 | 160 | 1895.9 | 1895.5 |
| finstorm | 60 | 60 | 60 | 2.6 | 3.7 |
| mixed512 | 8 | 12 | 8 | 39.4 | 36.4 |

v3.1 借助年轻代预算的周转松弛在各处恢复了 v2 的收集节奏（ptrdense
46 → 16，mixed512 12 → 8），并消除了 M4 时期真实场景的墙钟时间回归
（见 `/var/tmp/gcbench/report.md` 中 §14 的真实场景表；3 次取中位数）：
bootstrap 52.6s → 48.1s（v2：51.6s —— +1.9% 的回归变为 −6.8%），
tinyriscv 编译 20.5s → 17.7s（v2 18.9s），LSP 40.4s → 38.8s
（v2 43.7s），tinyriscv 仿真 1.30s → 1.28s（v2 1.79s）。作为松弛的
代价，峰值 RSS 相对 v3.0 上升（bootstrap 1031 → 1231 MiB，LSP
1100 → 1247 MiB），但在所有场景仍低于 v2（1423/1306 MiB）；峰值堆按
构造被约束在 2×live + young。
- **平台**：linux x86_64 + aarch64，`CROSS=win64`（llvm-mingw）——
  所有 span 代码都是纯 C；唯一对架构敏感的部分是既有的栈指针汇编
  （gc.c:272-288）以及 `_aligned_malloc` 与 `aligned_alloc` 之别。

## 15. 里程碑

| M | 交付物 | 门槛 |
| --- | --- | --- |
| M0 | 基准测试套件 + v2 基线数字（`make gc-bench`、gc_torture 扩展） | 基线表已记录 —— **完成** |
| M1 | span 堆的**分配**侧（§4–§6）；标记仍按对象跨 span 进行 | gc_torture + 完整 `make test` + `make unittest` —— **完成** |
| M2 | `EMPEROR_GC_MODE=greentea` 之下的 green-tea 标记循环 + 清扫（§7–§9） | 前述门槛 + greentea 下全部 GcTest + 自举不变 —— **完成** |
| M3 | 目标启发式、`gc_info` 统计、调优 | 基准门槛（§14）—— **完成**（标记 −79..−99.8%，deepsurvive 快 4.8×） |
| M4 | 切换默认；删除 v2 分代机器；停止发射屏障；ABI 标签提升；文档 | 全部门槛 + 两个平台 + 自举重新收敛 —— **完成**（M4a 切换 + 2 个切换门槛缺陷修复；M4b 删除；M4c 屏障退役 + 标签提升；自举重新收敛；make test 1563/0） |
| v3.1 | 统一双堆目标（§9.2 勘误）：malloc+span 之上的同一个预算，`max(min_heap, factor×live) + young`，删除 `gt_update_goal` | 重跑相同门槛 —— **完成**（gc-bench 节奏与 v2 持平：ptrdense 46→16，mixed512 12→8；自举墙钟相对 v2 −6.8%（52.6→48.1s），GC 12.3→8.0s，19 次 full；三个真实负载在墙钟时间和 RSS 两方面都胜过 v2；make test 1563/0） |

## 16. 未来工作

- **SIMD 位图内核**：对 64 字节 AVX-512/GFNI 块做 `gray & ~black`
  （Go 博客中的 `VGF2P8AFFINEQB` 技巧），仅限 x86_64，经 CPUID 门控，
  标量内核作为回退。Go 报告可再降低约 10%。
- **无头部槽位**：从 span 推导大小/类别，仅为可 finalizer 的类保留
  元数据 —— 每个对象省回 24 B。
- **`_emperor_alloc_impl(size, flags)`**：增量式 ABI，让代码生成能标记
  无 finalizer 的分配，使 span 整体回收达到 O(1)（无需析构器探测）。
- **安全网退役**：一旦 `GC_VERIFY` 差分在完整测试语料 + 自举 +
  gc_torture 上长期保持干净，即可移除隔离区环与保守式栈覆盖。
- **并发标记**：只有在多 OS 线程下才有意义；会重新引入写屏障
  （起始快照）—— 在运行时保持单赋值器期间明确不在范围内。

## 17. 参考文献

- golang/go#73581 —— runtime：green tea 垃圾回收器（设计 + 数据）：
  <https://github.com/golang/go/issues/73581>
- Go 博客：The Green Tea Garbage Collector：
  <https://go.dev/blog/greenteagc>
- Go 1.25 发布说明（实验特性）、Go 1.26（默认）：
  <https://go.dev/doc/go1.25>、<https://go.dev/doc/go1.26>
- 当前实现：`EmperorPenguin/std/c/gc.c`（v2）、`gc_torture.c`、
  `include/emperor_gc.h`、`include/emperor_types.h`（ref-map 编码）、
  `scheduler.c`（协程栈）、`penguin_jit.cpp`（JIT 分配路径）
- 发射器侧（本设计未改动）：`EmperorPenguin/src/llvm/LLVMEmitter.penguin`
  —— 轮询发射（4475-4482）、帧描述符（1642-1667）、元数据/ref-map
  表（2637-2845）、分配点（4934/5123/5489/5507）、屏障点（4289-4318）
- 实现计划：`.agents/plans/gc-greentea-span-heap.md`
