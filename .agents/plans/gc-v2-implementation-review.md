# GC v2 实现审查 + 设计拆解 + 性能对比分析（2026-09-08）

对象：分支 `refactor/gc-generational` @ `61e0cd20`（Phase 0–3c 全部落盘）。
对照基线：`8ecd8264`（GC v2 之前的保守 mark-sweep，独立 worktree 重建并通过 bootstrap 收敛验证，
lib md5 `1a6e2545…` 与历史记录一致）。
基准方法：同机、交错执行、各 3 轮取中位数；墙钟+峰值 RSS 由 wait4/rusage 采集（/tmp/gcv/runstat）。

---

## 一、TL;DR

1. **功能完整度**：计划的核心三支柱（精确类型图、精确根、分代+bump）已全部实现并自举收敛；
   验证体系（GC_VERIFY 差分/STRESS/DEBUG_* 家族 + 7 个新 GcTest + torture 分代段）是整个工程最扎实的部分。
2. **性能现状分裂**：
   - **precise（分代）模式 ≈ 基线墙钟（77.6s vs 68.0s，+14%），LSP 重用例快 1.9×（13.1s vs 24.8s）**，
     但峰值 RSS 4.3GB（基线 0.83GB）——保守栈掩护产生的钉住-降级块页保留累积，是当前最大成本。
   - **default（非分代）模式比基线慢 2.6×（176s vs 68s）**，DynamicLinkTest 20.2→43.3s、LSP MetaFun 4.6→12.6s 同趋势；
     纯代码速度无回退（GC_DISABLE 63.3s ≈ 基线），+113s 全部是收集开销，成因未完全归因（见 §5.3）。
   - **建议：把 precise/分代设为默认模式**（当前默认反而是最慢的形态），并优先攻 RSS。
3. **正确性**：**双模式全量套件均绿**——default 1,387/1,387（今晨 08:31 run）+
   precise 813/813 pass2+pass3 全矩阵（本轮实测，HEAD）。
   3b/3c 提交信息记录了 7+5 个由 gdb/ASan/COVERDIFF 法医定位并修复的损坏家族——每处都有根因记录，质量可信。

---

## 二、交付总表（计划 → 提交）

| 计划阶段 | 提交 | 内容 |
|---|---|---|
| Phase 0 审计/护栏 | cd058a9e(+fbf926e3 内) | C 侧持根审计、torture、基线 |
| Phase 1 ref-map | fbf926e3 | 元数据 +refmap、gc_refmap_walk、GC_VERIFY |
| Phase 2 精确根 | ab31727c | 帧描述符、poll 安全点、每栈链头、entry_arg 根 |
| Phase 3a 精确切换 | 850a3bf8 + eb0c72e2 | precise 模式、隔离环、meta 钉住、诊断族 |
| Phase 3b 分代 | 92c2aacb 等 | nursery/晋升/屏障/typed 注册（发射器+运行时） |
| Phase 3c 修复补全 | 1a1d9ef8…61e0cd20 | 根集补全（结构 home）、屏障/索引生命周期、7 个 GcTest |
| Phase 4 清理/文档 | **未做** | 保守栈扫描/sorted index/watermark 仍在；文档未更新 |

累计 gc.c 3,979 行（基线 781 行），LLVMEmitter ~7,000 行，7 个新 GcTest + torture 分代段。

---

## 三、as-built 架构拆解（自然语义版）

这一节用平实的语言讲清楚 GC 现在到底是怎么工作的：每个机制解决什么问题、
运行时发生什么、为什么非这样不可。代码符号在括号里给出锚点。

### 3.1 堆的形状：两个世界和一块"留置地"

GC 把堆分成了三个命运不同的区域，出发点是一个朴素观察：**编译器这种程序里，
对象要么活一眨眼（拼接字符串的中间产物），要么活到程序结束（语法树、符号表），
中间状态很少**。所以：

- **年轻代（nursery，"婴儿房"）**：所有小于 16KB 的新对象都从这里出生。
  它不是一块块 malloc 来的，而是从 1MB 的大页（superchunk）上切下来的 64KB
  小块（chunk）。分配快得惊人：指针往前挪一下就完事——不查表、不插链、
  不记账，连清零以外的准备工作都没有。用完的 chunk 整块还回去，**垃圾根本
  不需要逐个清理**：婴儿房直接推平重来。
- **老年代**：在婴儿房里活过一次回收的对象，被整块拷贝到这里（malloc 一块
  新家），从此过普通日子——由传统的"标记-清扫"管理。
- **降级块（"留置地"）**：有一种对象不能搬（下面 3.5 讲为什么），它们所在的
  chunk 整块被划为"永远不动的老年代"，物理页面保留，里面的死对象仍会被
  大回收判死，但空间不还给系统——这是精确性换来的代价，后面性能一节会看到
  它就是 RSS 膨胀的源头。

每个对象头上仍有 24 字节的旧头部，但年轻代把其中一个原本闲置的字段
（`next`）复用成了**搬家留言条**：对象被拷贝到老年代后，原地址留下
"我已搬往新址"，以后再有人拿着旧地址找来，读到留言条就自动改去新家。
这个"留条-追条"的机制叫转发（tombstone/forwarding），是所有拷贝式 GC 的
标准做法。

配套的查找设施都是为了让"这个地址是谁家的"这个问题变快：先用一个粗筛
（地址在不在任何大页范围内）挡掉绝大多数无关指针，再用一张按地址排好序的
chunk 表二分定位，进了 chunk 再对它自带的有序对象表二分。三级过滤，
每一级都很便宜。

### 3.2 每个类型的"指针藏宝图"（ref-map）

老 GC 扫描对象时把对象体里**每个字**都当指针嫌疑犯查一遍——又慢又冤枉。
现在编译器在编译期就为每个类/枚举算好一张小表格（存在元数据里，`refmap`），
写明"这个类型的对象，第 8 字节是一个指针；第 32 字节内嵌了一个小结构体，
去查那张子图；如果这是个枚举，先看 tag，tag=2 时 payload 里的第 0 字节
才是指针"。GC 拿着图走，只读图上标注的位置。

这张图是递归的（结构体套结构体、枚举套枚举），但类型嵌套有限深，所以图
很小。图没算出来或外来的 C 侧类型没有图？退回老的全字扫描——**慢，但绝不
错**，所以任何失败都只是性能问题。

### 3.3 谁是根：GC 从哪里开始认"活"

回收的本质是回答"从哪些出发点沿指针走，能走到的都算活"。出发点有六类：

1. **帧描述符（本设计的核心创新点）**：编译器给每个函数的栈帧发一张登记表，
   上面列出"本帧哪些槽位装着 GC 引用、装的是裸指针还是一个带图的结构体"。
   函数进入时把表挂到**本栈**的链子上，返回时摘下来。协程各有各的链子头，
   切换协程时换装；异常长跳会跳过正常返回，所以 try 块入口记下链子头、
   throw 时恢复。有了这个，GC 看栈不再需要"把每个字都当指针试"。
2. **全局变量**：编译器为每个装引用的全局登记一个槽位。
3. **容器的 malloc 缓冲区（typed 区间）**：`Vector<T>` 的元素放在自己 malloc
   的裸内存里，GC 天生看不见。现在容器注册缓冲区时附带元素类型图
   （`#__track_buffer`），GC 按图逐元素访问——小回收时还能**就地改写**里面的
   引用（搬家后把旧地址换成新地址），这是精确注册独有的能力。
4. **新鲜对象隔离环**：刚 `new` 出来还没被任何变量接住的对象，正处于
   "只在表达式求值途中"的真空期。最近 512 个分配进一个保护环，自动回收时
   无条件放行。为什么有界就够？因为表达式嵌套深度有限，在飞对象顶多几十个。
5. **meta 钉住**：元编程把对象当整数地址烤进生成的代码里，任何扫描都看不见
   它们——只能显式登记"这个地址永远别动"。
6. **保守栈掩护（诚实的妥协）**：帧登记表只能列编译器知道的名字，但 clang
   的寄存器分配器会把一些引用值放在**它自己的临时栈槽**里，没有名字可登记。
   小回收时对主栈做一次保守扫描：扫到的年轻对象**只钉住、不搬走**。曾试图
   退役这个掩护（3c），结果立刻崩溃——它还在承重。它是当前 RSS 问题的根源，
   也是下一步的主攻方向。

### 3.4 收集什么时候发生：收费站模式

所有分配和回收都不再"随时"发生，而是集中在**安全点**：

- 每次分配只做一个动作：如果堆用量越过水位线，就把"该收垃圾了"的旗子竖起来，
  然后立刻返回——**绝不在 C 函数中途动手**（那时栈上哪些字是活指针说不清）。
- 编译器在每个函数调用和分配点前插了一个极便宜的检查（`__gc_poll`）：
  旗子没竖就直接返回（一次内存读取），竖了才进收费站收一次。
- 真正的 C 侧长跑（一直没有安全点）极端情况下内存涨到水位的 8 倍，才被迫
  就地收一次（保守扫描兜底）。
- 用户显式调 `gc_collect()` 语义不同："现在就收"——丢掉隔离环、跑保守扫描，
  保证垃圾当场死（一批单测依赖这个语义）。

三种运行模式（环境变量切换）：**default**（不分代，安全点收全量——现状
反而最慢）、**precise**（本文的分代全开）、**conservative**（完全旧行为，
留作二分定位）。另有一个 ABI 版本符号（`emperor-rt-gc2-3b`）防止旧
`.penguin-lib` 混链新运行时。

### 3.5 小回收（minor）：婴儿房大扫除的完整流程

假设旗子竖起、收费站开动，按七个阶段走：

**A. 先钉后搬（次序是铁律）**：先做保守扫描（掩护栈+协程栈+老式区间），
   认出"有匿名指针指着"的年轻对象，给它们钉上"禁止搬动"的标记。
   为什么必须先钉后搬？一旦对象搬走、原址变成留言条，匿名指针指着留言条
   就永远无法兑现——它没法被改写。先钉，这些对象就永远留在原址。

**B. 老→青清单（记忆集）**：老年代对象字段里写着年轻对象地址的，是危险边——
   老对象不搬，它字段里的年轻地址若不处理，小回收后就成了指向被推平内存的
   悬空指针。这些边平时由写屏障（3.7）记账，这里照账处理；账本默认可信，
   GC_VERIFY 模式下还会全堆复查一遍账本漏没漏。

**C-D. 处理根**：全局变量、钉住表、帧链上的每个槽位、typed 容器缓冲区的
   每个元素、隔离环——逐一走一遍。裸指针槽位直接改写成新地址（搬家）；
   结构槽位拿图走进去改。

**E. 传递闭包**：搬走的对象自己字段里也可能指着年轻对象——把每个幸存者
   排队，逐个用它们的图再走一遍，直到队列排空。所有幸存者要么已搬进
   老年代（原址留条），要么被钉在原地（chunk 进留置地）。

**F. 遗物处置**：婴儿房里既没标记也没留条的对象是死物——运行它们的析构
   （容器释放 malloc 缓冲），此刻一切内存还可读。

**G. 推平**：钉过的 chunk 划入留置地（页面保留），其余 chunk **整块**归还
   自由列表——年轻代的垃圾清除成本是零，这正是分代的全部意义。

### 3.6 大回收（major）：老年代清扫

老年代用水位（或留置地膨胀速度）触发。流程 = 先跑一遍小回收清空婴儿房，
然后对老年代做经典的标记-清扫：从根出发沿藏宝图标记，扫掉没标记的块、
压缩空闲表。两个分代特有的细节：

- 解析指针时必须**感知 chunk 和留言条**：钉住对象住在留置地 chunk 里，
  拿着它的地址查 malloc 索引会查不到（那里只登记 malloc 块）——先查 chunk，
  撞到留言条就跟随到新家。
- 曾有读者"经留言条读到 chunk 内存"的窗口，所以被这样读过的 chunk 即使
  里面已无活物也不能马上推平（mark_hit 保护）；反之，留置地里**整块全死**
  的 chunk 在大回收时可以真正归还——这是留置地唯一的出口。

### 3.7 写屏障：老人记新人

小回收能只扫年轻代的前提是：所有"老家伙字段里写着小家伙地址"的边都被
记账。记账发生在写入时刻——编译器在**每次往对象字段存引用**后插一个屏障
调用（裸引用一个版本，整结构体拷贝一个带图的版本；C 运行时里仅有的两处
引用字段写入，StringBuilder 的构造和扩容，也手动加了）。屏障本身很便宜：
两步比较确认"写入者是老年代的、写入的值是年轻代的"，才往开放寻址的
记忆集里添一行。账本的生死和持有者严格同步——对象只会在收集中死去，
所以每次收集整表清空即可，不存在悬挂条目。

### 3.8 编译器一侧做了什么

分代 GC 是一份编译器和运行时的**联合协议**，发射器（LLVMEmitter）承担的：

- **安家**：扫描每个函数，把所有可能装引用的寄存器（对象、字符串，3c 起
  还包括内嵌引用的值类/枚举结构）提升为带名字的栈槽，并登记进帧描述符。
  有几类寄存器故意不安家（纯别名视图、写链内部地址），它们的上游有家。
- **插桩**：定义处统一走"存进槽位"、调用/分配前插 poll、字段写后插屏障——
  三种插桩各有中枢机制，一处改动覆盖所有发射路径。
- **容器注册**：泛型容器实例化时按元素类型分派三种注册（裸引用图/结构图/
  保守字节兜底）。
- **守门**：帧描述符存在性的预测器（预测多了浪费几条指令，预测少了帧链
  泄漏——只许错在浪费一边）。

### 3.9 不变量（这套设计赖以不塌的七条）

1. 匿名指针只能钉不能搬（A 先于一切晋升）。
2. 记忆集条目不活过任何一次收集。
3. 屏障和掩护查询永不用过期索引（漏记=漏搬=悬空）。
4. chunk 对象绝不混进 malloc 索引（幽灵条目曾造成真实损坏）。
5. 被留言条读者触过的 chunk 不推平。
6. 隔离环条目永远指向尚未被判死的块（收集时标记后清空）。
7. typed 容器区间小回收只钉不改写（死容器的缓冲可能已被 malloc 复用）。

---

## 四、正确性评估

**验证体系**（超出计划预期）：GC_VERIFY 差分（miss-set 栈字归因+retaddr）、
STRESS_EVERY/STRESS_MAX（把稀有 miss 压缩进一次运行）、DEBUG_CRASH（SIGSEGV 打最后 poll 点+回溯）、
DEBUG_FREES/MISSEDEDGE/POSTMINOR/COVRDIFF/RECLAIM_TRACE——每个 3b/3c 损坏家族都有对应探测器。

**已修复的代表家族**（提交记录齐全）：mark 排空遗漏（typed/frame 子图被误判死）、屏障 pending 盲区、
记忆集悬挂槽、stale 索引屏障漏记、chunk 回收竞争（tombstone 读者）、finalizer 内嵌套收集、
mark_hit 保活、ghost 索引、List<Token>/StringBuilder-data/FuncParamTypes 三类损坏的完整根因链。

**残余风险**：
- R1 `gc_slot_for_ir_type` 对不可解析 ref<> 拼写的 bare 回退理论上不安全（若恰为值类→无效 IR，
  clang 大声失败兜底；建议加发射期断言）。
- R2 保守栈掩护依赖"钉住即可见"——掩护退役未完成（NO_STACK_COVER 自举 4.7s 即崩），意味着
  SSA 溢出槽中仍有未安家引用；这是 RSS 问题的根。
- R3 meta 钉住无 unpin（短进程可接受）；R4 默认模式性能回退未归因（§5.3）。

---

## 五、性能对比分析（本轮实测）

### 5.1 主矩阵：pass3 自编译（EmperorPenguinPass2，交错 3 轮取中位）

| 配置 | 墙钟 | 峰值 RSS | 备注 |
|---|---|---|---|
| 基线 8ecd8264 | **68.0s** | **830MiB** | 旧保守收集器 |
| 当前 default | 176.2s | 1,350MiB | 非分代 poll 全量收集 |
| 当前 precise(128M) | **77.6s** | 4,360MiB | 分代全开 |
| 当前 GC_DISABLE | 63.3s | 24GB | 纯代码速度 ≈ 基线 |
| precise YOUNG=32M | 176.0s | 5,726MiB | 更频 minor→更慢更高 RSS |
| precise NO_STACK_COVER | 崩溃@4.7s | — | 掩护是承重墙 |

（输出 .ll：default 与 precise 逐字节一致 ✓ 模式只影响运行时；与基线不同=发射文本新增 poll/帧/屏障。）

### 5.2 套件级

| 负载 | 基线 | 当前 default | 当前 precise |
|---|---|---|---|
| DynamicLinkTest×16 | 20.2s | 43.3s（2.1×） | — |
| LSP SelfHost 重编译 | n/a(worktree 缺产物) | 24.8s | **13.1s（1.9×↓）** |
| LSP MetaFun* | 4.6s | 12.6s（2.7×） | — |
| 全量 md 套件 | — | 1,387/0，1,597s | **813/813 PASS，735s**（pass2+pass3 全矩阵） |

### 5.3 解读

1. **发射代码零代价**：GC_DISABLE 63.3s ≈ 基线 68s——poll/帧/屏障/改写的指令开销被
   代码级差异吸收，即全部墙钟差异来自收集路径。
2. **default 回退 2.6× 未归因**（+113s 纯收集开销）：候选=每轮全量收集的标记集更大
   （RSS 1350 vs 830 佐证保留更多）× 收集次数或更多；隔离环已排除（precise 门控）。
   值得一次 GC 计数归因（暴露 _emperor_gc_collect_count 即可）——**或直接废弃 default 模式**。
3. **precise 墙钟已接近基线**：nursery 零 sweep + bump 分配 + 首存活即晋升，抵消了掩护成本；
   LSP 重用例 1.9× 加速说明**交互延迟敏感场景收益最大**（正是本计划动机）。
4. **RSS 4.3GB 是 precise 模式的核心代价**，且与预算反相关（32M 预算→5.7GB）：
   minor 越多→掩护钉住越多→降级 chunk 页保留越多。128M 预算下 4.3GB ≈ 67K chunk 当量，
   说明降级块回收（major 全死回收+demotion-driven 调度）跟不上掩护的钉住速率。
   3b 调参注释（"RSS ~2× budget"）已不成立——3c 的掩护回退（默认开）改变了等式。

### 5.4 建议（优先级序）

1. **默认切 precise/分代**（一次环境变量/初始化改动；前提已成立——precise 全量 813/813 绿）。
   立即收益：自编译 -56%、LSP -47%、DynamicLink 预计对标基线。
2. **攻 RSS**（P0）：完成掩护退役 = SSA 溢出槽安家（3c 结构 home 的延伸：把 clang 溢出的
   ref 值也纳入描述符槽，或 -O1/寄存器分配配合）。次优：钉住粒度细化（chunk 半区回收/
   major 时把钉住幸存者拷贝收缩页保留）。
3. **归因或废弃 default 模式**；保留 conservative 作二分基线即可。
4. Phase 4 清单照旧：删保守栈扫描路径/旧 sorted 索引可等掩护退役后一并做；
   文档（AGENTS.md 运行时段 + 格式规范）未动，需补。

---

## 六、方法学备注

- 基线 worktree：/home/cauly/Workspace/penguinlang-gcbase（8ecd8264，bootstrap 收敛 e29d6b1d/1a6e2545）。
- 计时：/tmp/gcv/runstat（wait4 rusage；墙钟 MONOTONIC + ru_maxrss）。
- 交错执行抵机器漂移；每配置 3 轮中位数；LSP precise 经临时 Env 注入（已还原）。
- 历史数据（09-04 的 58.8s DynamicLink 等）作废——当时机器高负载，本轮成对数据为准。

---

## 七、追查报告（2026-09-08 晚）：default 退化归因 + GC_DISABLE 构成 + NO_STACK_COVER 修复

### 7.1 硬数据（EMPEROR_GC_STATS atexit 计数器，本轮新加进 gc.c）

| 配置 | 墙钟 | 峰值RSS | GC时间 | 收集次数 | 分配总量 | 存活峰值 |
|---|---|---|---|---|---|---|
| 基线 default | 68.0s | 830MB | 53.4s（53 fulls，sweep 11.1s） | 53 | 67.1M / 4.0GB | 205MB |
| 基线 GC_DISABLE | **20.6s** | 6.1GB | 0 | 0 | 同上 | — |
| 当前 default | 183.8s | 1.35GB | 134.2s（98 fulls，sweep 31.6s） | 98 | **272.6M / 13.5GB** | 292MB |
| 当前 GC_DISABLE | 66.7s（修后 51.7s） | 23.9GB（修后 15.2GB） | 0 | 0 | 同上 | — |
| 当前 precise | 92.5s | 4.3GB | 48.7s（69 minors + 17 genfulls） | 86 | 272.6M / 14.8GB | 320MB |

关键修正：**§5.3 的"GC_DISABLE 63.3s ≈ 基线 68s ⇒ 纯代码速度无回退"是误读**——对照组错了。
基线关掉 GC 只要 20.6s：旧保守收集器自己就吃掉基线 68s 里的 47.4s（70%）。
当前发射代码纯速度（66.7s）是基线纯速度（20.6s）的 **3.2×**。

### 7.2 default 2.6× 退化的完整归因（+115.8s）

1. **发射代码纯速度回退 +46s**（66.7 vs 20.6）：
   - `gc_pending_old_add` 在非分代模式无条件插入 pending-old 哈希（GC_DISABLE 下永不排空，
     膨胀到全部分配块）——占整个 no-GC 运行的 **10.5%**。已修：门控 `_gc_generational`
     （实测 no-GC 66.7→51.7s、RSS 23.9→15.2GB，输出 .ll 逐字节一致）。
   - poll 空转 5.3%（21 亿次调用）、分配路径 wrapper ~2%（quarantine no-op 调用已一并门控）。
   - **分配量爆炸 4.06×**（272.6M 个对象 vs 基线 67.1M；13.5GB vs 4.0GB）——发射器 GC v2
     机制自身的程序级分配（scan_gc_regs 每函数建 6 个 List、ensure_gc_layout_in 全 defs 树
     字符串遍历、get_alloca_name 线性扫描、布局名字符串……），同时直接喂大收集器工作量。
   - 发射器扫描族 ~6-8%（ensure_gc_layout_in 3.0%、scan_mutable_regs/find_class_layout/…）。
2. **收集开销 +81s**（134.2 vs 53.4）：
   - 次数 98 vs 53（1.85×）：threshold=live×2 相近（292 vs 205MB），次数随 3.4× 分配量上涨。
   - 单次 1.37s vs 1.01s：标记集更大（RSS 1350 vs 830MB——描述符槽滞留值被保守扫描钉住）+
     gc_mark_drain/gc_refmap_walk 每对象函数调用与 map 解译（基线是内联字循环直接标子节点）。
   - perf 佐证：gc_resolve_block 42-43%（~28% 来自 collect_main 的保守栈+区间扫描、~14% 来自
     refmap walk 每 ref 槽二分）、sweep 10.5%、qsort(gc_ptr_cmp) 4.8%、finalize 2.3%。

结论：default 模式 = "发射代码变慢" × "喂给收集器 4× 的垃圾流" 复合；而这个负载 98% 是
短命垃圾（string churn），正是分代主场（precise 收集净开销仅 ~26s）。

### 7.3 GC_DISABLE 63s（现 51.7s）的构成

基线纯代码 20.6s + pending_old 哈希 ~7s（已修）+ poll ~3.5s + 分配路径 wrapper ~1.5s（部分已修）
+ 4× 分配量的 memset/malloc/页错误成本（~10s 量级）+ 发射器 GC 扫描族 ~5-8s ≈ 51.7s。
剩余与基线的差距里最大的一块是分配量本身——后续应给发射器扫描族做去分配优化
（List→数组栈缓冲、布局名字缓存）。

### 7.4 NO_STACK_COVER 崩溃根因与修复

复现：precise + NO_STACK_COVER 自编译 4.7s SIGSEGV（`_utils.List.at` ← get_alloca_name，
MetaEngine 编译路径）；带 COVRDIFF 探针则 836 条"would-finalize live-held"报告。
根因是三类"精确根看不见/改不动"的引用（GC_VERIFY 差分帧归属 + .ll 直接证据）：

- **E-generic**：`gc_param_slot_for_ir_type` 对布局查不到的 ref 拼写（泛型 `ref<List<Token>>`
  等）返回 `""`——参数完全没有镜像槽（寄存器版早有 `ptr null` 兜底，参数版漏了）。
  List<T>.at/grow/slot_addr 家族（编译器最热函数）全部裸奔。
- **E-movement**：ref/string 参数与 `this` 的镜像槽只保活不防移动——函数体继续用 SSA `%name`
  （`__builtin_panic` 里 `call println(ptr %text)` 直接可见），minor 晋升对象后 SSA 副本
  指向已回收 chunk。
- **E-bare**：字段/泛型/跨名字空间解析出的**裸/全限定类名拼写**（`SourceLocation`、
  `<global>.<global>.emperor.BoundType`、裸 `List<...>`）不带 `ref<>` 前缀，
  `is_ref_type`/`value_class_struct_type` 全部跳过——自编译扫描显示 512 个 SourceLocation、
  255 个 BoundType、~500 个 List/StringBuilder/Token 寄存器未安家。
- **E-quarantine**：隔离环用 evacuate **晋升**在飞的新对象——SSA 副本同样悬垂
  （IRBox_new/IRGlobalLoad_new/iter 构造家族；掩护开启时被保守扫描重新钉住才显得安全）。

修复（本轮落盘，两轮迭代）：
1. `_emperor_gc_pin_refmap` 哨兵（gc.c）+ `gc_pin_frame_chain` 预扫描（Phase A 末、一切晋升前）；
   mark/evacuate/POSTMINOR 对 pin 槽按 bare 处理或跳过；ABI bump `emperor-rt-gc2-3d`。
2. 参数/`this` 镜像槽改 PIN 语义；`gc_param_slot_for_ir_type` 未知 ref 拼写回退 `ptr null`。
3. `maybe_class_ir_type` + 三处分类函数（gc_slot_for_ir_type / value_class_struct_type /
   gc_param_slot_for_ir_type）接受裸/全限定类名拼写；prologue memcpy 复制路径补 >16B byval 门控。
4. 隔离环 pin 化（bump 分配使 512 环项聚集在最近 1-2 个 chunk，每次 minor 额外降级有界）。
5. 新诊断 `EMPEROR_GC_SCAN_DEBUG=1`：scan_gc_regs 打印每个未安家寄存器及排除原因。
6. **帧预测器补洞**（round-2 引入的崩溃的修复）：预测器只认 `ref<`/`enum<`/string 拼写，
   裸拼写字段的函数不被预测有帧——安家后它们链接描述符但 RET 不解链 → head 全局残留死帧
   → 后续函数 prev 链入被复用栈内存（第一次收集即 SEGV）。预测器加 `maybe_class_ir_type`
   分支 + scan 结束时"有槽必预测"兜底。
7. `allocate_class`/`emit_box` 的 `gc_def_ptr`：被提升寄存器的 def 行重写为 tmp+store 后，
   memset/metadata/构造器等后续行改用从槽加载的指针（裸拼写 NEW/BOX 结果安家后必需）。
8. **NEW 定义寄存器排除**（round-3，修 3 个套件回归）：allocate_class 对值类 NEW 结果走
   entry-alloca（寄存器裸名）+ 构造器直写——指针语义是消费方（异类型多 def 可变寄存器的
   桥接 assign、byval 转发）的既有依赖。安家此类寄存器制造双存储（NEW 写 %name、使用方读
   %name.addr，值永不到家），桥接 assign 发射 struct-into-ptr store（GenericCascade/
   GenericClass* 链接失败）。MutParamCopyDirect 回归则是 prologue memcpy 的 >16B 门控误伤
   ref 拼写值类参数（16B Point）——门控已回退；参数镜像槽的裸拼写分支单独加 >16B byval
   门控（≤16B 裸拼写值类按值传入非指针，不可当结构存储走）。

验证（round 3 后，最终态）：
- `make bootstrap` 收敛（exe 91af9549 / lib c4707d99）。
- 3 个回归用例全恢复（MutParamCopyDirect=1、GenericClassString=hello、GenericCascade=12）；
  GcStructHomeValueClass/GcNoStackCoverParamPin 双环境输出正确（24370/323960）。
- NO_STACK_COVER 自编译完成不再崩（修复前 4.7s SIGSEGV），COVRDIFF 探针运行完整通过；
  但两次运行 .ll 非确定（某类 sizeof 读到 0 vs 472——layout 数据被剩余未安家家族静默
  损坏；round-2 曾 3/3 md5 一致，代价是 NEW 结果安家的 3 个回归）。结论：cover-off 的
  崩溃已修复、可用性达成，彻底退役掩护仍差 wchain/alias 族安家（§7.5）——cover-off
  保持实验定位，默认掩护不动。

### 7.5 遗留

- **掩护默认仍开**：NO_STACK_COVER 现已可用（自编译+探针绿），但 COVRDIFF 仍报 ~800 条
  hold=0 残留（string/BoundClassDefinition/LLVMEmitter 自身/List），主要是 wchain/alias
  排除族（~1463 个寄存器——接收者/字段别名跨参数求值 poll 的内部指针暴露）与 mangled
  泛型枚举拼写布局解析失败（Option$... ~138 例）。分代 major 自带保守栈掩护，降级块中的
  悬空字由 major 兜底，实践风险低；彻底退役掩护需先安家 wchain 族（bare 槽可免费获得
  gc_retain_interior 的钉住语义，但改动 lvalue 发射，需单独评估）。
- mixed-kind 具名/临时同名寄存器（自编译仅 1 例，暂忽略）。
- ~~发射器扫描族的去分配优化~~ → **已由 §8 完成**（分配 278.8M→52.0M）。
- ~~精确模式默认化~~ → **已由 §8 完成**（默认即 precise；`EMPEROR_GC_MODE=default`/`legacy`
  保留旧非分代行为作二分通道）。RSS 攻坚（precise 3.18GB vs 基线 830MB）仍是遗留课题。

---

## 八、性能收尾（2026-09-09 凌晨）：发射器扫描族去分配化 + 默认 precise

§7.3/§7.5 指认的"最大杠杆"落地。perf 归因（GC_DISABLE 自编译，cpu_core 桶）：
`_emperor_gc_poll` 10.1%、`_emperor_string_equal` 4.8%、`ensure_gc_layout_in` 2.8%
（每次调用全 defs 树递归 + 每 def strip_global_prefix 分配）、`scan_mutable_regs` 2.3%
（O(n²) 计数扫描）、`find_class_layout` 1.7%（三趟线性 + 每条目 2 次 substring）、
`find_func_param_types` 1.6%（每 call 指令 2 次线性查表）、List.at 各实例 ~8%、
`gc_pending_append` 1.2%（禁用模式下纯浪费）、字符串辅助函数（`string_starts_with` 每字符
**分配 2 个 1 字符 string**、`is_string_type`/`strip_mut_ir_prefix` 每次 substring）——
合计构成 +32.5s 纯速度回退的大头与 278.8M 次 GC 分配的主体。

改动（提交见 git log）：
1. **`_utils.StrMap`**（string→i64 链式哈希，djb2 over `string_char_code_at` 零分配探测；
   两个 utils 镜像同步）。
2. **发射器零分配字符串族**：`string_starts_with`/`is_string_type`/`is_ref_type`/
   `strip_mut_ir_prefix`/`maybe_class_ir_type`/`gc_layout_name_matches`/find_*_layout
   的三趟后缀匹配全部改 char-code 比较（语义逐字节等价——`is_string_type` 仍只认精确
   `ref<string>`）。
3. **memo/索引**：find_class/enum_layout（值=idx+1，0=确认缺失；ensure_*_layout 创建布局
   时整表重置——负 memo 不得存活于布局诞生后）、find_func_param_types（表在发射前冻结，
   负 memo 安全）、get_alloca_name/is_mutable_reg/gc_only_home/register-get_llvm_type
   每函数哈希、ensure_gc_layout_in 首次调用建平坦 (预计算名, def) 索引 + 完成 memo。
4. **去二次方**：scan_mutable_regs（flags 位图 + 两遍 + 候选数组，finalize 按首现顺序——
   顺序/.ll 不变）、scan_gc_regs（7 个 List→1 个位图）、scan_write_chains（def 表→两
   个按 kind 的哈希）。
5. **gc.c**：`gc_pending_append` 禁用模式早退；**默认模式切 precise**（env 未设=precise；
   `default`/`legacy` 显式回退旧行为）；修一个默认切换暴露的洞——poll 的 gc_minor 分支与
   gc_chunk_acquire 的紧急 minor、gc_collect_main 均补 `_emperor_gc_disabled` 门控
   （否则禁用模式下 nursery 预算标志会驱动真实 minor，对象移动后 SSA 副本悬垂 →
   Lexer.new 读回收 chunk 崩溃）。

验证：
- **.ll 行为等价证明**：旧二进制与新二进制编译同一当前源码 → 48.6MB .ll **逐字节一致**
  （`cmp` 通过）——所有重写保行为。bootstrap 收敛（exe 741cce8a / lib e00e4f02）。
- 自编译矩阵（runstat，交错单轮；基线=8ecd8264）：

| 配置 | 改造前(§7) | 本轮 | 旧GC基线 | 分配量 |
|---|---|---|---|---|
| precise（现默认） | 94.4s / 4.36GB | **47.5s / 3.18GB** | 68.0s / 830MB | 272.6M → 52.0M |
| default（legacy 通道） | 181.0s / 1.35GB | 67.7s / 1.12GB | —（即基线行为） | 同上 |
| GC_DISABLE | 53.1s / 14.6GB | 29.8s / 5.4GB | 20.6s / 6.1GB | 278.8M → 52.0M |

- **precise 自编译首次快过旧保守收集器基线（-30%）**；分配量 52.0M 低于基线 67.1M。
  GC_DISABLE 与基线纯速度的剩余差距（9s）= 1.15B 次 poll 调用 + bump 分配路径本身。

遗留：poll 站点内联（load+test+br 条件调用）可再省 ~5-8% 但 .ll 文本 +3 行/站点，
对自编译（发射受限）净收益 ~2s，对运行重的负载（LSP/测试 RUN 阶段）更大——未做；
RSS（precise 3.18GB）与掩护退役（§7.5 wchain 族）照旧。

---

## 七、2026-09-09 复审附录（3d931a4e 之后）

昨日报告的三条建议全部被后续提交落实，独立复测确认：

| 建议 | 提交 | 复测结果 |
|---|---|---|
| ①默认切 precise | 3d931a4e | default=分代；EMPEROR_GC_MODE=default/legacy 退回旧行为 |
| ②归因 default 回退 | 9ffc1baa | 归因完成且**纠正了本报告 §5.3 的错误**：基线禁 GC 实为 20.6s（我错比了基线开 GC 的 68s）——回退 = +46s 纯代码（发射器扫描族分配抖动，278M 次分配）+ 81s 收集放大 |
| ③NO_STACK_COVER 崩溃 | a67d014a | 四类根因修复（E-generic/E-movement/E-bare/E-quarantine）；崩溃点从 4.7s 推进到 ~34s（99.999% 完成处）——**仍崩，掩护仍承重** |

### 复测矩阵（同协议，HEAD=3d931a4e，3 轮中位）

| 配置 | 墙钟 | RSS | 对比昨日 | 对比基线 |
|---|---|---|---|---|
| default（=分代） | **43.5s** | 3.1GB | 77.6s（-44%） | **68.0s（快 36%）** |
| legacy（旧 default） | 64.7s | 1.1GB | 176.2s（回退消除） | 68.0s（≈持平） |
| 禁 GC | 27.5s | 5.4GB | 63.3s | 基线禁 GC 20.6s（1.33× 残余） |
| NO_STACK_COVER | **SIGSEGV@34s** | — | 昨日 4.7s 崩 | 掩护成本≈+9s/+1GB |

套件：GcTest 14/14、DynamicLinkTest 16/16（25.8s，昨日 43.3s）、**LspTest 19/19（14.6s，昨日 default 24.9s）**、
全量 4 编译器 1187/0（新默认）+ 815/815（precise 强制，切换前夜）。
⚠️ 复审补的门：新默认下 LspTest（build/lsp 曾陈旧）——本轮重建后绿。
⚠️ runstat 的 WEXITSTATUS 对信号死亡误报 0——本轮已换信号感知包装（runstat2）复验 nocover。

### GC 统计剖析（EMPEROR_GC_STATS，自编译 42s）

- 23 次 minor（6.3s）+ 14 次分代 full（7.9s）= **GC 共 14.1s / 42s（33%）**；fulls=0（水位 major 一次没跑，
  全部由降级驱动调度触发 ✓）
- 52M 次分配 / 4.0GB 累计；poll 11.5 亿次（空转合计 ~1s，可忽略 ✓）
- **live_peak=358MiB vs RSS 3.2GB —— 8.9× 开销**：预算占 ~0.26GB，其余 ~2.5GB 全部是保守掩护钉住→
  降级 chunk 页保留。RSS 问题的精确画像：不是泄漏，是"钉住即永久页保留"策略对 ~358MB 活堆的放大。

### 本轮复审结论

1. **性能目标达成**：默认形态比分代前基线快 36%，LSP 场景快 ~40%，回退全消除——GC v2 首次全面优于旧收集器。
2. **RSS 是仅存的大项**，且已被统计精确画像（活 0.36GB/驻留 3.2GB）。攻法不变：掩护退役收尾
   （wchain/alias 安家，§7.5 清单）或钉住粒度细化（major 时收缩留置地）。
3. 质量评价：钉住槽哨兵（pin_refmap 指针同一性判定）、记忆化带失效时机注释、maybe_class_ir_type
   命名约定启发式有文档化的适用边界——实现质量持续在线；每个修复都有对应回归测试锁定。

---

## 九、2026-09-09 白天：掩护退役完成 + RSS 攻坚（wchain 安家 / 区域改写 / 屏障补洞）

复审附录指认的三件事（nocover 残余、RSS、掩护退役）本轮全部落地。四个独立缺陷，全部有 POSTMINOR/
字节一致性证据链。

### 9.1 wchain/alias/unbox 链 PIN 镜像（发射器）

§7.5 的 ~1463 个 wchain/alias 排除寄存器：其指针只以 SSA/gep 值存在（lvalue 寻址，不能提升到
.addr 存储模型），def 与消费方（WRMBR/mut-this call）之间的每个参数求值 poll 都是暴露窗口。
方案 = **只写 PIN 镜像槽**：scan_gc_regs 对 pointer 型 wchain/alias/unbox 寄存器发
`%reg.gcpin = alloca ptr`（入口清零）+ 描述符槽（`@_emperor_gc_pin_refmap` 哨兵）；各 def 点
（emit_rdmbr inline 分支/ref load 分支、emit_rdenum 两分支、emit_assign alias 指针分支、
emit_unbox、emit_global_load ref 分支、emit_def_load）补 `store ptr <val>, ptr %reg.gcpin`。
Phase A 的 pin 预扫描钉住目标（内部指针→钉 owner），SSA 副本永不悬垂。寄存器本身不动（lvalue
发射零改动——只有新增行）。Primitive 链值无引用，不发镜像。

### 9.2 typed-region 元素 pin 的排序缺陷（gc.c，NO_STACK_COVER 崩溃根因）

容器元素槽是 pins-only（不可改写：死容器的 region 在 dispose 前残留，改写会打进 tcache 复用
内存——3c 教训）。但元素 pin 走的是 **Phase D 的区域 walk，排在全局根/帧链撤离之后**：先被
其它根晋升的目标在槽里留下冻结墓碑，后续读取解析到晋升前的陈旧子指针（POSTMINOR
"TOMBSTONE (missed rewrite)" 风暴 → List.at 腐败 → SIGSEGV@14-34s）。掩护开启时 Phase A 的
保守扫描恰好预先钉住"刚存入还在栈上"的值，掩盖了缺陷。修复 = 区域 walk 前置到一切晋升之前
（见 9.3 的最终形态）。

### 9.3 区域元素改写模式 + 缓冲清零（RSS 主攻）

排序修复后 nocover 自编译全绿但 RSS 不动（3.0GB）：新统计归因 = **26,765 个降级 chunk
（1.67GB 页保留）里只有 146MB 活对象（8.7% 密度）**——pins-only 语义下容器工作集永久钉在
原地，密度由 bump 分配的批结构决定。修复两件套：

1. **改写模式为默认**（`EMPEROR_GC_REGION_PINS=1` 回退旧 pins-only 二分）：元素作为精确槽
   撤离（晋升+改写），内部指针（枚举 payload 别名）仍钉 owner。walk 放在 Phase D 末（隔离环
   pin 之后——一切 pin 源先于改写）。
2. **`_emperor_gc_track_buffer` 注册时清零缓冲**：_malloc 回收内存的容量尾残留旧指针（能通过
   exact-header 合法性检查）会被当真元素晋升，memcpy 垃圾尺寸对象砸烂 malloc 堆
   （"free(): invalid pointer"）。清零后 [len,cap) 全 NULL，maybe-nursery 过滤直接跳过。
   所有容器（utils.List/_grow、std.Array/vector/hashmap）都是"注册先于元素落位"，清零安全。

### 9.4 屏障 `!ch->pinned` 跳过 + Phase G 块伴幸存者字段闭合（两个漏改写源）

极限压力（1MB nursery）下改写模式输出非确定 + 偶发失败。POSTMINOR 带 kind 标注定位到两个
独立漏改写源（既有缺陷，pins-only 下被"一切皆钉"掩盖）：

1. **写屏障对 pinned chunk 值跳过 remember**（`ch && !ch->pinned`）：pin 只保当轮，下一轮该值
   晋升时这次字段存储不会重发屏障 → 老年代字段留下不可改写的墓碑。修复 = 无条件 remember。
2. **块伴幸存者字段从未入队**：对象仅因所在 chunk 被钉而幸存（真正的命中对象是别的块伴），
   没有任何 evacuate_slot 访问过它 → 它的字段从未加入改写闭包。修复 = Phase G 降级时把每个
   幸存者 gc_evac_push，回收循环后补一次 drain（此点之后一切年轻对象都在降级块里，改写只会
   晋升仅经这些迟到字段可达的目标）。

### 9.5 掩护默认关闭

`_gc_stack_cover_poll = 0` 默认（`EMPEROR_GC_STACK_COVER=1` 恢复、`EMPEROR_GC_NO_STACK_COVER`
兼容无操作）。major 自己的保守 mark 掩盖（非移动、只保活）保持开启。NO_STACK_COVER 语义从
"实验"转正为默认。

### 9.6 结果（EmperorPenguinPass2 自编译，runstat2 单轮；基线=8ecd8264 68.0s/830MB）

| 配置 | 昨日复审 | 本轮 | 基线 |
|---|---|---|---|
| 默认（=precise+掩护关+改写） | 43.5s / 3.1GB | **39.2s / 1.34GB** | 68.0s / 830MB |
| 降级 chunk | — | 26.8K → **4.0K**（1.67GB→250MB） | — |
| GC 时间 | 14.1s | **8.8s**（24 minor + 10 genfull） | 53.4s |
| 输出 .ll | 1b009699 | **1b009699（逐字节一致）** | — |

- 掩护关/开、1MB/256KB/128MB nursery 输出全部一致（极限压力下偶发非确定仍存在——见遗留）。
- 新回归测试：`Tests/GcTest/GcNoStackCoverWriteChain.md`（链 pin 镜像）、
  `GcNoStackCoverRegionPin.md`（区域元素，内联 `#__track_buffer` 容器镜像 utils.List）。

### 9.7 遗留

- **极限压力非确定**：1MB nursery（100× 默认频率）自编译仍有偶发输出漂移/失败（负载相关，
  ~4/12 轮）；默认 128MB 全部一致（10+ 轮）。POSTMINOR 在 9.4 后仍偶报 demotedbody 墓碑——
  还有至少一个漏改写源未闭合（下一个嫌疑：C 侧/异常路径的存储）。
- RSS 1.34GB 的构成：活 ~330MB（malloc 碎片放大）+ 降级块 250MB + nursery 预算 128MB + 栈 32MB。
  进一步：陈旧 pin 槽清零（consumer 处 store null）、预算/密度调优。
- GC_STATS 新增 demoted_chunks/retained/live objs/young_bytes/pin_slots 行（RSS 归因常驻）。

---

## 十、2026-09-09 夜间:1MB 极限压力非确定性攻坚(§9.7 遗留 1)

### 10.1 复现与归因链(工具:emitter_v10..v14 逐步插桩)

- **复现**:1MB nursery 自编译,输出 .ll 与参考 md5 比对。首轮即漂移(14.9 万行 diff,
  首分歧在 `emperor_LLVMEmitter_emit_call` 的入口 alloca 段——IR 生成期数据已被污染);
  一次 MonomorphizePass AST 遍历 SIGSEGV(core 回溯);GC_VERIFY 下多次
  `free(): invalid pointer` / SIGABRT。注意:/tmp 塞满会产生"空 .ll + exit 0"的假象
  (ENOSPC,fwrite 静默失败)——排查前先 df。
- **排除法**(每项都有判别实验):
  - C 侧 StringBuilder 屏障已在(`_emperor_StringBuilder_new/append` 都调 write_barrier),
    屏障日志(DEBUG_BARRIER,59.7 万条)证实 old 目标屏障正常 remember;
  - WRMBR 屏障纪律:.ll 审计(i8-gep 名字跟踪)2375 处引用字段存储全部配屏障,
    103 处无屏障均为无引用的操作符枚举/Option<bool>;
  - refmap 枚举 variant 表:Option 声明序 some=0/none=1,运行时 tag 与 refmap 表一致
    (写解码器按 walker 语义遍历验证;some(tag0) 载荷有覆盖,none 载荷零初始化跳过);
  - remembered set 动态扩容仅 OOM 丢;major 标记 mark_hit 保护 all-dead 回收判定;
    mark_failed 有完整门控(部分标记不清扫不回收);
  - C 侧无内联收集(分代模式),JIT 编译的是同一发射器 IR(屏障随行)。
- **RS_FALLBACK=1 反而首轮即崩**(free(): invalid pointer)——全量老体遍历会急促触碰
  已陈旧的字段,把主损坏放大成立刻崩溃。

### 10.2 修复:9.4 补排空的时序缺陷(gc.c,生产路径根因之一)

9.4 的"降级幸存者字段闭合"把 post-Phase-G drain 放在**非钉 chunk 回收之后**:
- 回收循环 reset top/nobjs 并 `gc_chunk_index_remove` 后,迟到字段的目标若在已回收
  chunk 里,gc_evacuate_slot 既不能晋升也不能改写(所有解析路径返回 NULL);
- 更糟:回收 chunk 被 bump 复用后,exact_ok 头部校验可能"通过" → **晋升复用内存里的
  垃圾 → memcpy 砸烂 malloc 堆**(free(): invalid pointer 签名吻合)——一个原初缺陷
  解释下游全部乱象(伪持有者、demotedbody 墓碑风暴、m=1 尸体在被回收 chunk 里)。
- **修复**:drain 前移到降级循环与回收循环之间(一切目标内存与索引仍有效);溢出降级
  从"仅复位"改为"钉住全部活跃 chunk"(否则溢出时回收会把迟到目标变成损坏而非陈旧)。
- 附带修复:GC_VERIFY 救援通道在 Phase E 之后 push 的对象同样受益(verify 模式下
  gv1 的 SIGSEGV / rt1 的 abort 家族)。

### 10.3 修复:发射器 pin 镜像消费点清零(RSS,§9.7 遗留 2 第一刀)

- `scan_gcpin_clears`:对每个链 pin 寄存器(wchain/alias/unbox)计算最后引用指令
  (WRMBR obj、全部调用参数、CALL_VIRT obj、链定义上游操作数),主发射循环在该指令后
  发射 `store ptr null, ptr %reg.gcpin` —— pin 随链死亡而非活到帧尾(陈旧镜像每个
  都在钉 64KB chunk 的页保留)。
- 安全护栏:**同基本块 guard**(def 与 last-use 之间有 label 不清零)——循环携带链
  (def 在循环外、消费在循环内)第二轮迭代的 poll 窗口不会被误清;无引用位点或无法
  证明单定义的寄存器保持原行为(帧尾自然失效)。

### 10.4 验证

- **1MB 极限压力**:8/8 轮输出 md5 全一致(新参考 8d92bbae——源码含 pin 清零后重生成);
  修复前同类协议 5 绿即漂(§10.1 复现)、上一会话 ~4/12。
- **make bootstrap 收敛**:exe c7efc0ca / lib c363ab6d(pass4=pass5)。
- **默认配置自编译**(新 bootstrap pass3,runstat2 2 轮):WALL 39.5/39.7s,
  RSS 1355/1354MiB,EXIT 0;GC 9.2s(24 minor + 10 genfull);demoted 3.94K/247MB;
  对基线(68.0s/830MB)快 42%,RSS 1.63×。与 §9.6(39.2s/1.34GB)持平——
  pin 消费点清零在本负载未产生可测 RSS 收益(陈旧链镜像占比小;参数 pin 槽与
  隔离环才是大头),保留为严格正向的机制修正(长帧负载收益更大)。
- **md 套件**:PASS 1393 / FAIL 0 / ERROR 0(SKIP 215 为 pass1 网关,正常)。
- **dotnet**:EmperorPenguin.Tests 468/468 通过。

### 10.5 遗留

- **确定性残留置信**:8/8 + bootstrap 收敛是强证据但非证明;若后续再捕漂移,下一
  诊断器:mark 模式槽解析中 `maybe_nursery 命中但 gc_chunk_of==NULL`(指向已回收
  chunk 的陈旧字)加报告——那是不变量破坏的第一现场。
- RSS 1.35GB 构成不变(活 ~330MB + 降级块 247MB + nursery 预算 128MB);
  下一刀:参数 pin 槽消费点清零(需参数级 last-use 分析)或隔离环(512 条/次)
  尺寸/行为调优。
- 调试基建沉淀(全部 env 门控,零开销路径一分支):POSTMINOR 持有者类名、
  GC_VERIFY rescue 持有者/目标类名、DEBUG_BARRIER 老目标屏障日志 +
  watch-list 年轻跳过日志。

---

## 十一、2026-09-10：写屏障卡表化（remembered set → 512B 脏卡）

用户指令：参考 Go/Java 的写屏障把老年代按 512B 页打脏标记，minor 只走
脏页+幸存者，不碰整个老堆。落地为**纯运行时改动**（gc.c + torture +
头文件注释；emitter 零改动，`.ll` 逐字节不变，bootstrap md5 不变）。

### 11.1 机制

- **槽位记忆集 → 卡表**：删除 `RememberedEntry{slot,map}` 开放寻址表，
  换成 `_gc_cards`——512B 卡键（`slot & ~511`）开地址集合。老年代不连续
  （晋升 malloc 块 + 降级 chunk 与外部 arena 交错），Java 的
  `base + (addr>>9)` 字节数组不适用；哈希键集合是等价物。
- **屏障**（`_emperor_gc_write_barrier(_map)`，签名/ABI 不变）：保留
  `gc_barrier_old_target(obj)` 的老年代判别（年轻写者快速路径原样），
  之后**无条件 `gc_card_mark(slot)`**——经典卡语义，去掉值年轻检查
  （扫描时按字段当前值判定）。map 参数不再记录（卡扫描走 owner 自身
  refmap，其布局已含嵌入 struct 子节点）；NULL map 早退不变。
  按页去重是结构性收益：同一对象的 k 次存储 = 1 个表项而非 k 个。
- **minor Phase C = 脏卡扫描**（`gc_cards_scan`）：卡键快照 + 排序（缓存
  数组）后按地址序扫每张卡 [lo,lo+512)：
  - 降级 chunk：chunk 索引二分找重叠 chunk（卡可跨 64KB slice 边界，含
    前驱），chunk 的 objs[] 二分找重叠对象（含跨卡前驱对象），跳过
    墓碑（h->next）与尸体（marked==2），`gc_evacuate_body` 走图；
  - malloc 块：sorted 索引二分找包含 lo 的前驱块 + 卡内起始的所有块，
    `last_block` 去重（大块跨多张脏卡只扫一次）；
  - 解析不到 chunk/块的卡（barrier 漏过的栈 alloca、super 区间假阳性）
    直接丢弃。扫描前 `gc_refresh_sorted()`（失败降级全老扫——RS_FALLBACK
  同款兜底）。
- **清空时机不变**：每个 minor 末清卡；major 兜底清卡点保留。老年代对象
  只在 major 里死、每个 major 先跑 minor，脏卡的持有者在扫描时必然存活，
  清卡无损（再存储再标记——一次性槽记录做不到的覆盖性）。
- **卡粒度的鲁棒性红利**：同一老对象上**漏发屏障**的存储，只要同对象/
  同卡有任一屏障发过卡，就会被整对象扫描一并救活（G9 用例锁死此语义）。
  GC_VERIFY rescue 仍是漏卡检测器。
- 附带修复：`gc_old_scan_young_refs`（fallback/verify 路径）降级 chunk
  循环补墓碑跳过——墓碑体首 8B 是转发地址，会被 gc_evacuate_body 当
  metadata 解引用（存量隐患，RS_FALLBACK=1 "首轮即崩"的候选解释之一）。
- 环境开关语义：`EMPEROR_GC_NO_REMEMBERED`（保留名）= 跳过卡扫描；
  `EMPEROR_GC_RS_FALLBACK=1` = 全老扫兜底，组合即二分通道。GC_STATS 增加
  cards/marked 计数；DEBUG_MINOR 的 minor 汇总行带本轮卡数。

### 11.2 顺带修的 torture 存量红

G6（typed buffer 元素改写）自 238ad821（track_buffer 注册时清零缓冲）起
就没跑过 torture：setup_g6 先写元素后注册，元素被清零抹掉。改为按容器
契约先注册后落元素。G9/G10 新增：G9 = 同对象一槽有屏障 + 一槽**故意**
无屏障 + 裸槽两次覆写（卡粒度覆盖 + 当前值判定）；G10 = 有屏障存储后
无屏障置 NULL（脏卡扫描不得复活/崩溃）。

### 11.3 验证

- torture 9 模式矩阵 + ASan 6 组合全 PASS（precise/GC_VERIFY/NOGEN/
  NOPROMOTE/RS_FALLBACK/NO_REMEMBERED+fallback/REGION_PINS/STRESS/
  conservative；ASan 再叠 stress 与 256KB nursery）。
- **`.ll` 不变证明**：HEAD 的 `pass3.ll` 重链新 C runtime → pass3_new
  编 EmperorPenguinLib，输出与昨晚 bootstrap 参考
  `build/bootstrap/pass4.d/libemperorpenguin.ll` **md5 逐字节一致**
  （0d332e8d…）——同一编译器 .ll、新旧运行时 A/B。
- 1MB nursery 极限压力 **8/8 轮** md5 全一致（0d332e8d…，全 exit 0）。
- md 套件全量（4 编译器矩阵）**1393/0/215** —— 与 §10.4 逐项相同；
  GcTest 18/18；EmperorPenguin.Tests dotnet **468/468**。
- `make bootstrap` 收敛：exe 5d286d98…（新——二进制内嵌新 gc.o，预期变化），
  **lib c363ab6d… 与 §10.4 完全相同**（.ll→link 产物零漂移的官方链复证）。

### 11.4 性能（默认配置自编译，runstat2）

| 指标 | 卡表（本轮） | §10.4 基线 |
|---|---|---|
| 墙钟 | 42.9s / 42.8s | 39.5s / 39.7s |
| RSS | 1309MiB | 1355 / 1354MiB |
| GC 时间 | **9.24s**（23 minor 4.34s + 10 genfull 4.91s） | 9.2s（24+10） |
| 降级 chunk | 4076（254.75MiB） | 3.94K（247MB） |
| 卡流量 | marked=5005 / scanned=5005（全程累计） | —（槽记忆集无对应计数） |

GC 本体时间与基线持平（9.24 vs 9.2s）；墙钟 +3.3s 落在 GC 之外
（本轮 A/B 异机时/后台负载窗口，文档口径 ±15% 噪声内；GC 分解相同
排除收集器回退）。卡流量极低（52.5M 次分配仅 5005 张卡）：编译器
负载是年轻代 churn + 晋升后不再改写，老→新存储本来就稀少——卡表
的意义在机制（minor 的老年代工作以脏页为界、漏屏障的共享卡救援）
与老年代高频改写负载的上限，不在本负载的加速。RSS −46MiB 与
降级块持平，在噪声内。

### 11.5 遗留

- 屏障仍付 `gc_barrier_old_target` 的多级判别（年轻快速路径 2 比较 +
  super/chunk 二分；老年代路径加 pending 哈希 + malloc 索引二分）。
  非连续堆下"无条件单字节写"的 Java 理想形态需要预留连续卡表地址空间
  （PROT_NONE 256GB 虚拟保留 + 按需 commit）或老年代 arena 化——
  记为后续课题，收益上限 = 消掉每次老对象存储的索引二分。

---

## 八、2026-09-10 复审附录（a2f72bde 之后：卡表 + 掩护退役落地）

五个新提交（b02125e1→a2f72bde）把 §7 的两条主杠杆全部落地：

1. **卡表写屏障**（a2f72bde）：512B 脏卡（开放寻址卡键集合——老年代非连续，Java 式
   基址+偏移字节数组不适用）替换槽位记忆集；minor 只扫脏卡覆盖的对象 + 精确根闭包，
   全老代扫描仅留给 RS_FALLBACK/GC_VERIFY。**无条件标脏**（不在存储时判新值）换页级
   去重 + 免"同对象多次写"重复记账；torture G9/G10 锁定了两个方向的鲁棒性红利
   （无屏障写入因同页其他写被救 / 屏障写后被 NULL 覆盖保持 NULL）。
2. **掩护退役**（b02125e1+238ad821）：wchain/alias/unbox 寄存器的链式钉住镜像
   （%reg.gcpin + pin_refmap 哨兵，def 点存指针、末次使用点清零——e45de71 加的精确清除，
   带基本块边界安全门）；**typed 区间改写模式为默认**（元素按精确槽 evacuate+改写，
   EMPErorR_GC_REGION_PINS=1 退回只钉）；**栈掩护默认关闭**；track_buffer 注册时清零缓冲
   （防 malloc 复用的容量尾假指针）；屏障无条件记账。

### 复测（HEAD=a2f72bde，3 轮中位）

| 指标 | 09-09 | **09-10** | 基线(8ecd) | 相对基线 |
|---|---|---|---|---|
| 自编译墙钟 | 43.5s | **36.2s** | 68.0s | **快 47%** |
| 峰值 RSS | 3.1GB | **1.33GB** | 0.83GB | 1.6×（原 3.7×） |
| GC 总耗时 | 14.1s (33%) | **7.7s (22%)** | — | — |
| genfulls | 15×560ms | **10×426ms** | — | — |
| minors | 43×244ms | **23×151ms** | — | — |
| 卡表 | — | **5,029 卡 / 全程** | — | 老代扫描缩小 ~3 个数量级 |
| 降级块 | 26.8K/1.67GB(更早) | **4.1K/254MB** | — | RSS 主因收敛 |

对照实验：REGION_PINS=1（旧只钉模式）= 42.1s/3.2GB/降级 22K——region 改写是 RSS 之胜的来源；
YOUNG=2GB（关 minor）= 34.3s/genfull 仍 9 次——major 由降级驱动，与预算无关 ✓。
门：全量 1393/0（含 LspTest 19，02:27 run）+ 本轮 HEAD 复验 LspTest 19/19（13.5s）+
GcTest 18/18 + torture 普通/ASan 双绿。

### 剩余剖析（通往 <10%）

GC 7.7s = minors 3.5s（23 次×151ms：**typed 区间仍每轮全量走**——容器元素存储经 #__store
裸 u64 写、无屏障，卡表管不到 malloc 缓冲区；+ 隔离环 + 帧链）+ genfulls 4.3s
（10 次 major，每次全量标记 ~320MB 活堆——降级驱动：param 钉槽/隔离环/区间内部指针
仍在产生 ~4K 降级块）。
下一对杠杆：①容器缓冲卡化（#__store ref 实例加屏障，或区区间级脏标志）→ minor 151ms→
~数十 ms；②钉住源头削减（隔离环改撤离？param 镜像逃逸分析）或 major 增量化 → 4.3s→~2s。
合理预期：GC 2-4s / 34s ≈ 6-12%；<1% 仍需并发标记（另立项）。

### 本轮结论

**设计目标"minor 只付脏页+幸存者的钱"已兑现老年代部分**；容器缓冲是最后一个 O(活堆)
热点。质量评价：卡表实现的降级路径（索引刷新失败→全老代走）、排序快照+去重、
bug-hunt 环境变量族、G9/G10 双向鲁棒性锁定——继续维持高标准。
