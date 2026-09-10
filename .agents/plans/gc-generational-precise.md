# GC v2：分代 + 精确扫描 + bump 分配器

分支 `refactor/gc-generational`。替换 `EmperorPenguin/std/c/gc.c` 现有保守 mark-sweep
（malloc 逐块 + 全堆字扫描 + 全栈/协程栈保守扫描 + sorted index 内部指针解析）。

## 目标

1. **分代**：短命对象（emit_line 字符串链等）由年轻代廉仔回收，不再全堆 mark。
2. **精确扫描**：栈根、堆体、容器缓冲、全局全部按类型图扫描；删除保守扫描与
   `gc_resolve_block` 内部指针解析（当前所有场景 45%+ 的 GC 热点）。
3. **bump 分配器**：年轻代 chunk 内指针递增分配，替代 malloc+memset+链表插入+pending 追加。

### 非目标

- 并发/增量收集（单线程程序：绿色协程在同一 OS 线程上切栈）。
- 压缩老年代（老年代保持非移动 mark-sweep；现有 sweep/tombstone 机器复用）。
- BabyPenguin VM（C# GC，不经此路径）；`--backend=cs` 不受影响。
- 向后兼容旧 `.penguin-lib` / 旧 metadata 布局（运行时 ABI 版本门拒绝混链，见风险表）。

## 勘察结论（设计依据）

| # | 事实 | 出处 |
|---|---|---|
| F1 | 分配入口唯一：emitted 侧仅 `@_emperor_alloc_impl`（NEW/BOX×2 三处发射点）；C 侧字符串族经 `_emperor_string_alloc → _emperor_gc_alloc(size,1)` | LLVMEmitter:3895,4239,4247；core_builtin.c:100-206 |
| F2 | `EmperorClassMetadata` 已有 `field_offsets/field_is_ptr`，但 marker 不用：is_ptr 只标记裸指针字段，内联值类（如 `SourceLocation{filename:string,...}` 整体嵌入父对象）与 `Option<T>{ptr,i32 tag,ptr payload}` 的嵌套指针会被漏扫 | gc.c:439-451；LLVMEmitter:2186；SourceLocation.penguin:5-27 |
| F3 | 枚举 = `{ptr metaptr, i64 tag, [N x i8] payload}` 不透明字节数组联合，构造时 payload 保证零初始化；payload 槽的指针性随 tag 变化 | LLVMEmitter:1988-2021, 4025-4112 |
| F4 | 可变寄存器已有 entry alloca；不可变寄存器是 SSA 值。**clang 链接全程无 -O（默认 -O0），所有值本就落栈槽** | LLVMEmitter:1176-1207, 4951-4976；emperor 脚本 link_exe |
| F5 | 有栈协程：32MB mmap 栈 + ucontext（Win32 fiber），调度策略在 Penguin 侧；GC 靠 watermark + 每栈保守扫描 | scheduler.c:77-155 |
| F6 | 容器（Array/Vector/HashMap/_utils.List）= `u64 buf` 裸 malloc + `_gc_scan_add/remove` 区间注册，**T 的指针性在实例化点静态已知但被丢弃**；`#__load/#__store` 只访问 malloc 缓冲，从不进 GC 对象内部 | vector.penguin:24-43；array.penguin:48-99；utils.penguin:103-176 |
| F7 | 全局：ref/string 全局 `_emperor_gc_add_root(void**)`（精确槽，就地解引用）；值类全局整 struct 内联、注册为保守扫描区间 | LLVMEmitter:1273-1293；gc.c:611-614 |
| F8 | JIT unit B 用同一 LLVMEmitter，符号解析进宿主进程 GC；但 JIT 模块无 @main —— 其 ref 全局今天根本没有根注册，只靠保守扫描侥幸存活 | MetaEngine.penguin:424-449；LLVMEmitter:939-957 |
| F9 | 新鲜对象保护 hack：分配触发的 collect 可能在指针返回前运行，靠临时 marked=1 护体 | gc.c:757-780 |

## 设计

三根支柱，按依赖顺序落地（每阶段独立可收敛、可提交）。

### 支柱 1：精确类型图（ref-map）

`EmperorClassMetadata` 增加一个 **ref-map 描述符**（C 侧 + emitter 侧同步改；不做旧布局兼容）：

```c
/* 一段紧凑数组，由 emitter 作为 private constant 发射，GC 解释执行 */
typedef struct EmperorRefMap {
    int kind;      /* PTR_SLOTS=0, STRUCT=1, ENUM=2, REF_ARRAY=3 */
    int n;         /* PTR_SLOTS: 偏移数; STRUCT: 字段数; ENUM: 变体数 */
    /* kind 后跟 int32 数组：
       PTR_SLOTS: n 个指针槽偏移
       STRUCT:    n 对 (field_offset, sub_refmap_id)
       ENUM:      n 个 sub_refmap_id（按 variant_idx；0=无指针 payload）
       REF_ARRAY: {elem_stride, ptr_off_in_elem}（容器缓冲/数组用，count 来自运行时） */
} EmperorRefMap;
```

- emitter 在 `ensure_class_layout`/`emit_field_arrays` 一并计算：类 → PTR_SLOTS 或
  STRUCT（内联值类字段递归引用子 map）；枚举 → ENUM（每变体 payload 的子 map）。
  引用图无环（递归类必为 ref 类型，字段是指针槽），天然可算。
- C 侧一个解释器 `gc_walk(map, addr, visit)` 供所有扫描点复用：堆体扫描、栈帧描述符、
  精确区间（容器缓冲/值类全局）、 remembered set 条目。
- **解释器是唯一正确性热点**：`GC_VERIFY=1` 模式下每次收集后对全堆做一次
  ref-map 遍历断言（载荷可解引用、枚举 tag 在界内），编码错误当场炸出而不是静默漏扫。

### 支柱 2：精确根（ref 槽位化 + 帧描述符）

- **所有 ref 型 IR 寄存器（含不可变的）一律 entry alloca 落栈**（可变的已是；不可变的从
  SSA 改为 alloca+load——F4 证明 -O0 下代价≈0）。枚举/值类局部 alloca 里的嵌套 ref 由
  ref-map 描述符覆盖。
- 每函数发射一个 **GC 帧描述符**：alloca 数组 `{slot_addr, refmap_id}[]` + 链表节点；
  函数入口 push、出口 pop（异常路径由 sjlj try 帧保存/恢复链头）。GC 就地改写槽内指针，
  **无需 reload/spill-reload 手写 statepoint**。
- **协程**：链表头改为 per-stack —— `EmperorCoroutine` 存各栈链头，切换时换装
  （scheduler.c ~50 行）。收集时精确遍历 主栈链 + 每个驻留协程链 + 全局根 + 精确区间。
  watermark/scan_set_live/每栈保守扫描协议整体退役。
- **C 运行时不持根不变式（关键决策）**：`_emperor_gc_alloc`（C 侧调用，如字符串族）
  **不再就地触发收集**——nursery 耗尽时直接挂新 chunk 继续分配。收集只发生在
  企鹅侧安全点（见支柱 3），此时所有活跃 ref 都在帧描述符里，C 帧一个不漏。
  兜底：chunk 预算耗尽 / mmap 失败时，允许 C 内就地收集，用**窄窗口保守扫描 + 钉住**
  （扫描 `[当前sp, 最深活跃帧描述符地址)` 的 C 帧窗口 + 各协程已保存的 regs jmp_buf，
  命中对象本周期原地保留不复制）——正确性优先、性能降级的罕见路径。
- **unit B 根注册**（修 F8）：emitter 为无 @main 模块发射 `__module_register_roots()`，
  MetaEngine 在 `penguin_jit_add_module` 后 lookup 并调用，注册 JIT 模块的 ref 全局与
  值类全局描述符；模块卸载/重编译时反向注销。
- 调试模式 `EMPEROR_GC_MODE=conservative`：保留旧保守扫描路径一个发布周期，供二分定位
  “精确漏根”类 bug（表现：好端端对象被收走 → 切回保守即可复现对比）。

### 支柱 3：分代堆 + bump 分配

```
年轻代: 64KB bump chunk 链（1MB mmap superchunk 切片）   ── 短命对象
老年代: 逐块 malloc + 精确 mark-sweep（复用现有 sweep/   ── 晋升者 + 大对象
        tombstone/分配链机器，删 sorted index）
大对象: ≥16KB 直接老年代分配（不复制）
```

- **分配快路径**（`_emperor_alloc_impl` / `_emperor_string_alloc` 共用）：
  `top + size <= end ? ptr=top, top+=size : 新chunk`；头部 16B
  `{u32 size, u8 gen, u8 flags(istring|forwarded|pinned), pad}`（GCHeader 从 24B 缩到 16B，
  不再需要 next/marked 常驻字段——年轻代靠 chunk 结构、老年代保留链）。
- **安全点 = 收集点**：penguin CALL/CALL_VIRT/NEW/BOX 发射点前插 poll
  （`load flag; cmp; br`，-O0 下相对 call 开销可忽略）；C 内部分配永不收集（支柱 2）。
  flag 置位条件：young 已用 ≥ 预算（默认 16MB，可 `EMPEROR_GC_YOUNG=` 调）或老年代
  阈值到点。到达 poll 即：先完成本次 spill 语义（ref 已全在槽里，天然成立）再收集。
- **小回收（minor）**：精确根（帧链×每栈 + 全局根 + 精确区间）+ remembered set →
  存活年轻对象 **首次存活即晋升**（拷贝进老年代 malloc 块，原槽就地改写转发指针；
  工作负载是双峰的：emit_line 串秒死 vs bound 树长寿，立即晋升近似最优且免老化半空间）。
  被钉住（兜底路径命中）的年轻对象整 chunk 降级为老年代 chunk 保留（页保留式，浪费上界
  = 1 chunk）。年轻代 chunk 链随后整批回收——**bump 空间整体归还，零 sweep 成本**。
  死年轻对象的 dispose_mem 在 minor 阶段运行（容器构造后即死的缓冲要释放）。
- **写屏障**：老年代堆槽的 ref 写入需要 remembered set。发射点：
  - WRMBR 裸 ref 字段 → `__gc_write_barrier(slot, val)`（slot 在老年代且 val 年轻 → 记 slot）；
  - WRMBR 内联值类/枚举整 struct 拷贝 → `__gc_write_barrier_map(slot, sub_refmap_id)`
    （记 {slot, map}，minor 时按图走嵌入式指针）；
  - GLOBAL_STORE 不需要（全局根每次 minor 全扫）；
  - 容器缓冲 `#__store` → T 为 ref 时改调 `__gc_write_barrier` 外包的 `__gc_store_ref`
    （容器泛型里 `#is_ref(T)` 一个新 intrinsic 分流；缓冲本身是 malloc 区间、每次 minor
    精确全扫，v1 不给区间分代）。
  记忆集 = 开放寻址槽表，minor 后清空。
- **大回收（major）**：老年代精确 mark-sweep。mark 走 ref-map（不再全字扫描），
  sweep/finalizer/阈值策略（live×2、低垃圾 ×4 增长）沿用现机器。
- 删除：sorted index 全套、`gc_resolve_block`、新鲜对象护体 hack（F9——收集只发生在
  返回指针之后的 poll 上，分配路径不可能先收集后返回）、setjmp 寄存器冲刷、
  `_emperor_gc_scan_add(bytes)` 旧语义。
- 区间注册 API 换精确版：`_emperor_gc_track_ref_buffer(base, count, stride, ptr_off)` /
  `_emperor_gc_track_refmap_region(base, refmap_id, bytes)`（值类全局）。

## Phase 0 审计结论（2026-09-04，已固化）

C 侧持根审计（精确化后需要显式处理的点）：
1. `co->entry_arg`（`_co_spawn_entry` 的 async ctx 对象）——C 侧长期持有的唯一 GC 引用，
   今天靠协程栈扫描区间覆盖。Phase 2：spawn 时 `_emperor_gc_add_root(&co->entry_arg)`
   （需配 remove API），`co_run_entry` 消费时注销。`_co_spawn_fn0` 的 entry_arg 是函数指针，免。
2. `_emperor_throw_msg`——已是显式根（scheduler.c:791-798）。
3. 其余 C 静态态（g_argc/g_argv、fd_waiters、spawn inbox、try 栈、指纹）均非 GC。
4. StringBuilder：`data` 是 GC 对象内 ref 字段（refmap 覆盖）；append 的 C 临时量
   （旧 data）经调用方溢出的 `this` 可达——"C 内不收集"下无问题。
5. ucontext/fiber 寄存器溢出缓冲：alloca 不变式下无独占引用，紧急路径可保守扫+钉住。
6. **sjlj 展开必须恢复帧描述符链头**：throw 长跳越过深层帧、不执行 epilogue，
   EmperorTryFrame 需保存/恢复链头，否则精确走图会踩陈旧槽。
7. C 侧需要**全协程注册表**（intrusive all-list in co_create/_co_destroy）——精确根
   遍历要枚举每条栈，Penguin 侧队列对 C 不可见。
8. `_emperor_ICopy_copy` 按**裸偏移**读 metadata（name@0, instance_size@8,
   core_builtin.c:1166-1172）——扩展 EmperorClassMetadata 时这两个字段位置不可动
   （refmap 追加到结构体尾部）。
9. `#address_of/#__load/#__store` 全库仅指向 malloc 缓冲（F6 确认）。编译期强制需要
   raw 指针类型系统区分 GC/malloc 地址，超出本次范围；Phase 3 移动化后违规立即崩溃，
   测试套件即守门。

**阶段范围调整**（比原计划更稳）：精确区间（容器缓冲 + 值类全局）从 Phase 1 移到
Phase 3（非移动阶段保守区间扫描本就正确，移动化才必须精确）；Phase 0 的分代
torture 用例随 Phase 1/3 落地（refmap 解释器 Phase 1 才存在）。

基线（8ecd8264，本机）：DynamicLinkTest 33s / LspTest 12.4s / bootstrap 收敛
exe 42420856… lib 1a6e2545… / stage A ≈77.7s stage B ≈11.5s（±15% 负载波动，
各阶段门以成对 A/B 复测为准）。

## 阶段计划（每阶段：改动 → 验证门 → 提交）

### Phase 0 — 护栏与补勘（不改正义代码）
- gc_torture.c 扩成分代拷问台：churn-存活校验、晋升后稳定性、转发指针、屏障
  （晋升后建老→新引用，minor 后验证槽已改写）、双代 finalizer、钉住路径。
- 补勘：scheduler.c 停车点/spawn inbox 中 C 侧持 ref 审计（`entry_arg` 等）；
  `#address_of` 全库用法确认仅指向 malloc 内存（今日成立，F6）→ 上升为编译期规则。
- 性能基线固化：LSP didChange、DynamicLinkTest、bootstrap stage A/B 秒数 + perf 采样存档。
- **门**：torture 在旧 GC 下全绿；基线数据入 `.agents/memory` 或本文件附录。

### Phase 1 — 精确类型图（ref-map 落地，仍非移动 mark-sweep）
- emitter：布局期计算 ref-map，metadata 全局加 `refmap` 槽；值类全局/容器区间注册
  换精确描述符（容器侧加 `#is_ref(T)` intrinsic 与 `__gc_store_ref`）。
- gc.c：堆体/区间/值类全局改精确走图；栈仍保守。
- **门**：`GC_VERIFY=1` 全套绿；`make bootstrap` 收敛（新 md5 固定）；全量 md 套件 +
  LspTest + DynamicLinkTest 绿；EmperorPenguin.Tests 的 BatchLLVMTest 期望更新
  （发射文本变了）。此阶段性能可能持平或小退（多走图 vs 少全字扫），记录即可。

### Phase 2 — 精确根（帧描述符，仍非移动）
- emitter：ref 寄存器全槽位化 + 帧描述符发射 + poll 占位（本阶段只置 flag 不分代，
  flag 到点走全量精确 mark-sweep）。
- scheduler.c：每栈链头换装；sjlj try 帧保存/恢复链头。
- unit B：模块根注册/注销（顺带修 F8 这个存量缺陷）。
- 切换开关：`EMPEROR_GC_MODE=conservative` 保留旧路径。
- **门**：同 Phase 1 全套 + **双模式差分**（同一 torture/套件分别在两模式下跑，
  结果必须一致）；关掉保守栈扫描后 32MB 协程栈不再被扫——LSP 场景应已可见收益。

### Phase 3 — 分代 + bump（支柱 3 全量）
- 新头部/chunk 机器/分配快路径/minor 晋升拷贝/ remembered set + 写屏障发射/大对象直老/
  双代 finalizer/钉住兜底路径；删除 F9 hack。
- **门**：全套回归 + torture 分代用例 + ASan torture + bootstrap 收敛；性能门见下。

### Phase 4 — 收尾
- 删保守路径与 sorted index 残余（或留一版后删）；`emperor_gc.h`/文档
  （Documentation GC 章节 + AGENTS.md 运行时段）更新；内存布局图；
  可选：分配快路径内联进 .ll（load/cmp/add 三指令，免 call）——仅在前面的门全绿后做。
- 最终 A/B 性能报告入 `.agents/plans` 本文件附录。

## Phase 3b 设计决策(2026-09-05,实现前固化)

**分代只在 `EMPEROR_GC_MODE=precise` 下启用**;default/conservative 行为与 3a 完全
一致(bootstrap/发布不受影响)。3b 全绿后(Phase 4)再把 precise 变为默认。
`EMPEROR_GC_NOGEN=1` = precise 但禁分代(所有分配走老年代 malloc 路径,非移动)——
分代 bug 二分用。

**对计划的两处有意偏离**(3a 实践教训:每一步都要独立可验证):

1. **GCHeader 保持 24B 单一布局**(计划原案年轻代 16B 头)。年轻对象也用
   `{next, marked, is_string, size}`,只是 next 不插链(chunk 步进遍历)、
   marked 在 minor 中当"已晋升/存活"位。一种头部 = mark/finalize/verify 机器
   全复用,省掉全代码两套布局分派。8B/对象的空间开销换一半实现风险。
2. **3b 拆成两个独立可验证的提交**:
   - **3b-1(纯 C 侧)**:nursery bump、minor 晋升、major=minor+老年代精确
     sweep、屏障/typed-region 的 C API、**remembered-set 缺失 fallback =
     minor 前全老年代 refmap 扫**(正确但慢)。emitter 零改动 → .ll 不变、
     bootstrap md5 不变、LLVMTest 不变;md precise 套件(801)即验证面。
   - **3b-2(emitter+容器)**:WRMBR 写屏障发射 + `#refmap_of(T)` intrinsic +
     容器 `_gc_track_buffer`(vector/array/utils.List;hashmap 经 Vector 继承)。
     此后 remembered set 真正生效,全老扫只在 GC_VERIFY 模式跑(遗漏检测)。

**机制要点**:
- **nursery**:1MB mmap superchunk 切 64KB chunk;chunk 元数据(YoungChunk
  {next,super,base,top,end,pinned})占数据区头 64B。分配 = 对齐 24+size 的
  bump;≥16KB 走老年代 malloc(大对象直老)。`EMPEROR_GC_YOUNG=` 预算(默认
  16MB)→ want_minor → poll 处 minor。
- **转发指针**:promote 时 memcpy 完成后源 body 首 8B 覆写新址(bump 分配的
  size 规整为 ≥8,永有空间);源 marked=1 即"已转发"标记。
- **minor 根集**(全精确,逐槽改写):全局根、meta pin 数组(直接改写)、帧链
  (当前+每协程,map 槽 walk 改写)、typed 区间(元素 walk)、remembered set、
  quarantine 环(无条件晋升,分代下语义天然)。**保守字扫**(协程栈区间、
  显式收集的主栈、紧急 C 内路径)命中年轻对象 → **钉住**:不改写、对象
  marked=1、所在 chunk 标 pinned;minor 尾声 pinned chunk 整块降级挂
  "老年代 pinned 链"(major 时步进遍历、mark 位复用、死对象只 finalize 不
  free,空间随 chunk 常驻——浪费上界 1 chunk/钉住事件)。
- **无 refmap 的对象/槽**(refmap=null,外来 metadata):walk 退化为保守
  body 扫 + 命中即钉住(不可改写)——统一规则,无特例。
- **写屏障**(3b-2):`_emperor_gc_write_barrier(obj_base, slot)` /
  `..._map(obj_base, slot, map)`。快路径:obj ∈ [nursery_min, nursery_max)
  两次比较即 ret(绝大多数写发生在年轻对象上);慢路径 resolve 老年代才查
  slot 内容。**slot 里已是新值**,无需传 val。GLOBAL_STORE 免(全局根每
  minor 全扫)、容器 `#__store` 免(typed 区间每 minor 全 walk)、NEW/ctor
  写免(对象年轻,快路径)、sret/参数免(栈)。
- **typed 区间**:`_gc_track_buffer(base, count, stride, elem_map)` +
  `_gc_untrack_buffer(base)`。elem_map 为元素类型 refmap 程序指针;裸引用
  元素传 C 侧预定义 `_emperor_gc_bare_refmap`(SLOTS {off 0, sub -1});
  原始类型元素不注册。与保守 `_gc_scan_add` 两张表并存:保守表在 minor 中
  只做钉住扫描,major 中照旧全字扫(非移动,安全)。
- **major = minor(显式/紧急带保守 cover) + 老年代 malloc 链精确 mark-sweep
  + pinned-chunk 链 sweep(死对象 finalize,空间不还)**。老年代阈值策略沿用
  现机器;`_emperor_gc_total_allocated` 只计老年代。
- **显式 `gc_collect()`** = major + 保守栈扫 cover(钉住),quarantine 清空
  ——与 3a 语义对齐,GcFinalizerTest/StdArrayAutoDispose 的"现在就收"成立。
- **紧急路径**:C 侧预算耗尽(挂不出新 chunk)→ 就地 minor(保守栈扫 cover,
  钉住)。C 内不再有"就地 major"。

## API / 布局变更清单

| 项 | 变更 |
|---|---|
| `EmperorClassMetadata` | +`const void* refmap`（field_is_ptr 可留作调试或删） |
| `emperor_gc.h` | `_gc_scan_add/remove(bytes)` → 精确 track 接口；+`__gc_write_barrier(_map)`、`__gc_store_ref`、safepoll 入口 |
| GCHeader | 24B → 16B `{size, gen, flags}`（老年代对象外挂链结点） |
| 发射文本 | ref 寄存器 alloca 化、帧描述符、call/alloc 前置 poll、屏障调用、ref-map 常量表 |
| runtime ABI | 新增 `_emperor_runtime_abi` 版本符号；`.penguin-lib` 装载与 JIT add_module 校验，不匹配干净报错（老 lib 拒绝混链） |
| 环境变量 | `EMPEROR_GC_YOUNG=`（预算）、`EMPEROR_GC_MODE=conservative`（过渡期）、`GC_VERIFY=1`、`EMPEROR_GC_DISABLE` 保留等义 |

## 风险与对策

| 风险 | 对策 |
|---|---|
| ref-map 编码错误 → 漏扫活对象（最危险） | GC_VERIFY 遍历断言 + conservative 差分模式 + torture 拷问台 + 阶段化（先非移动验证图，再上移动） |
| C 运行时隐藏持根（spawn inbox、C 局部跨分配） | “C 内不收集”不变式使常态无此问题；钉住兜底路径保正确性；Phase 0 审计清单 |
| 移动后原始地址失效（`#__load/#__store`、`#address_of`） | 现状仅指向 malloc 内存（F6）；Phase 0 后固化为编译期规则：取 GC 对象内部地址 → 编译错误（或显式 pin intrinsic） |
| 新发射代码在 pass1 受限特性下编不过 | 全部只用既有 IR 构件（alloca/store/call/全局常量），bootstrap 每阶段收敛即证 |
| JIT unit B / .penguin-lib 版本漂移 | ABI 版本符号强校验，拒绝混链 |
| 记忆集遗漏（某条老→新写路径没插屏障） | minor 时可选全老年代图扫校验模式（GC_VERIFY 扩展：找指向年轻代的未记录槽即报错） |
| 性能不达预期 | 每阶段独立测量；Phase 2 结束（仅去保守扫描）就应有显著收益，可提前止损 |

## 性能预期与测量

- 基线（当前，8ecd8264）：LSP didChange ~36s、DynamicLinkTest 33s、bootstrap stage A 77.7s /
  stage B 11.5s；GC 占比 didChange ~45%+（resolve_block 家族），发射阶段字符串链是分配风暴
  （emit_line ~5-10 alloc × 275K 行 .ll）。
- 预期：minor GC 拷贝近零存活 + 零栈扫 + 零内部指针解析 + bump 分配；
  didChange 有望 <15s，DynamicLinkTest <20s，stage A 显著下降（不承诺具体数，
  Phase 2/3 各出实测）。RSS 上界 ≈ live + young 预算（16MB）+ 老年代阈值余量。

## 工作量估计

gc.c 重写 ~1200-1500 行；LLVMEmitter ~700 行（ref-map 300 + 槽位化/帧描述符/poll 250 +
屏障 150）；stdlib 容器 4 文件小改；scheduler.c ~50；torture+校验 ~400；文档 ~200。
Phase 1、2 各约一个全量验证周期，Phase 3 最重。

## Phase 3b 根因闭环(2026-09-06 下午,precise 残余 bug 已破)

**症状族**:"指针一致的数据损坏"——受害者指针仍指向合法 chunk 内地址,内容被新
分配覆盖;崩溃点游走(List_at/append/concat/Lexer)。二分表中
"NOPROMOTE+cover 仍崩"的 cover 数据全部无效(见下)。

**根因链(gdb 全程取证,Repro B = pass3 编 EmperorPenguinLib + LD_PRELOAD 空 .so)**:
1. 帧描述符只登记发射器已知的槽(可变寄存器、参数、结构体临时)。**跨 safepoint
   存活的不可变 SSA 引用值**活在 clang regalloc 的私有溢出槽 / callee-saved
   寄存器里,任何描述符都指不了它们。
2. minor 晋升移动对象后,描述符槽被正确改写,但 SSA 溢出副本保持旧地址——读的是
   墓碑数据(碰巧完好,潜伏);若对象被判死则 chunk 回收、内存复用——硬损坏。
   实测:minor#1 晋升 io.penguin 源文本(合法),minor#21 地址被新分配覆盖,同时
   栈上有 11 个非描述符槽持有旧地址(全文扫描实证)。
3. 两个放大 bug(gc.c):
   - poll 入口预清 `_emperor_gc_want_collect` → gc_collect_main 的
     `explicit||want` 判 false → **full 触发被静默降级为裸 minor**;
   - 降级路径 `gc_minor(0)` 硬编码 cover=0,`_gc_stack_cover_poll` 在此路径
     不生效(所以 EMPEROR_GC_STACK_COVER 实验是 no-op,旧二分结论作废)。

**修复(gc.c,runtime-only,.ll 不变)**:
- `_gc_stack_cover_poll` 默认翻 1:每次 minor 保守扫主栈(命中即 pin 不动,
  setjmp 已刷 callee-saved 寄存器进扫描区间)——SSA 溢出副本结构性不可改写,
  "钉住不移动"是唯一 soundness 兜底;精确根照旧晋升/改写,cover 只增 pin。
  新 kill switch:`EMPEROR_GC_NO_STACK_COVER=1`(二分用)。
- poll 不再预清 want 标志,由 gc_minor(2199)/gc_collect_generational(2319) 等
  消费者完成后清理;full 触发恢复走 generational 全量。
- 降级路径 `gc_minor(_gc_stack_cover_poll)`。

**后续(Phase 3c 候选,真正的精确解)**:发射器给跨 poll 存活的 ref 型 SSA 值
配描述符槽位(alloca 化 + load/store),届时 cover 可退回显式/紧急路径。

## Phase 3b 根因闭环之二(2026-09-06 晚,track_buffer 元素 map 误判)

**第二 corruption 链(gdb 全程单 run 追踪)**:
1. `Vector<值类>` 的 `_grow` 用 `#__track_buffer(T,buf,cap,#sizeof(T))` 注册 buffer,
   T 的 IR 拼写是 `ref<X>`(值类/引用类**同拼写**)。
2. `gc_track_map_operand` 的 `ref<` 分支一律给 **bare map(只声明偏移 0)**——对
   引用类元素(单指针/stride 8)正确,对**值类内联元素(stride=sizeof(X),引用在
   X 自己的字段偏移,如 SourceInput 的 +8/+16)致命**:精确 walk 只访问偏移 0。
3. major 的 mark 经 `gc_resolve_block` 找老年代对象——容器里唯一的文本字符串引用
   不在任何被访问的槽 → 判死 → sweep `free()` → malloc 复用该块(List._grow 的新
   buffer)→ 元素拷贝砸烂字符串 length → `string_length` 返回指针 → Lexer
   `source_len` 巨大 → tokenize 失控循环(栈耗尽/垃圾读)。
4. 取证关键:出生证明(birth)= `file_read_text` 73183B;pending/qsort/merge/索引
   全正常(sorted V@0 在);mark 后 marked=0;free 时刻全内存扫描仅两个 typed
   region 持有引用,几何=stride 24/32、引用在 +16、map 只盖 +0。

**修复(LLVMEmitter.gc_track_map_operand)**:`ref<X>` 元素先 `ensure_gc_layout`
再 `is_value_class_ref` 判别——值类内联元素返回 `@X_refmap`(其自身字段槽),
无嵌入引用返回 "none";引用类/字符串维持 bare。发射产物变化:.ll 中值类容器的
track_buffer 调用从 bare 换成类 refmap(三模式间仍逐字节一致——GC 模式是运行时
环境变量,不影响发射)。

**两个根因的分工**:① SSA 溢出槽(cover 修复)管"栈上持有者的旧地址";②
本条管"容器元素的精确可达性"。两者都修后 precise 分代才 sound。

## Phase 3c 根因闭环之三(2026-09-07 凌晨,chunk 回收损坏拆成 5 个实证修复)

接"cover 常开 → old_pinned 膨胀"的回收尝试(3 秒语义损坏)展开,拆出 5 个
runtime-only 修复(gc.c,.ll 不变):

1. **major 保守解析 chunk-blind**:新增 `gc_resolve_any`(chunk owner+墓碑转发
   → malloc 索引),替换 mark_conservative / scan_regions_mark 裸分支 /
   frame bare slot / pinned 列表 / mark_object 回退共 5 处——之前它们只认
   `gc_resolve_block`(malloc sorted),minor cover pin 住的 demoted chunk 对象
   在 major 一个标不到。
2. **回收判据漏墓碑读者**:`YoungChunk.mark_hit`——任何 mark 解析命中该 chunk
   (含经墓碑转发到 malloc 副本的命中)都置位;全死回收判据改为
   `any_live = mark_hit || marked==1 存在`。栈/region 字指向墓碑、mark 转发走
   副本后,读者仍读墓碑内存,chunk 不可回收(RECLAIM_LEAK 不损坏的机理)。
3. **sorted 索引 ghost 污染**(致命):minor demote 循环把 chunk 对象
   `gc_pending_append` 进 malloc 索引(史前遗留)→ ghost 条目随 chunk 回收
   永不压缩(压缩只删链上对象)→ 67110 条 ghost 用垃圾 size 遮住真实块,
   resolve_block 解析错 base → 活对象漏标被 sweep。删除该 append。
   SYNC_TRACE 仪表:修复前 sweep#1 即 list=4/sorted=1004,修复后全程相等。
4. **demotion 触发 major 缺失**(性能螺旋):minor 尾 demoted chunk 数 ≥64 且
   ≥2×上次 major 计数 → want_collect(几何)。年轻 churn 负载此前 major 永不
   触发,old_pinned 无界膨胀(qsort+线性走双热点,600s 挂死)。
5. **typed region 元素 pins-only**(soundness):phase D 经 region 槽的改写
   (forwarding/promote)会写进"死容器迟一拍 untrack"的已释放 buffer
   (POSTMINOR 实证 5973 条注册、死容器 region 大量存在)→ tcache 复用后新分配
   出生即损。region 槽改为 `gc_retain_interior`(只 pin 不写)。精确 region
   语义(promote+改写)留给后续:dispose/untrack 时序或 region liveness epoch。

**结果**:conservative / NOGEN(precise 无 nursery)/ default 三模式全绿
(EmperorPenguinLib 自举 repro ≈139s,rc=0);**precise 分代仍有一个 ~83s
确定性 SIGSEGV 未决**——victim 非 sweep free(victim-ring 空)非 chunk 回收
(reclaim-ring 无命中),地址疑似 raw malloc/interior,即 `.tokens` 槽被写成
非法指针,写者未抓到。取证资产与下一步(HW watchpoint on .tokens)见
/tmp/states.txt。precise 模式继续标记 experimental。

## Phase 3c 根因闭环之四(2026-09-07 白天,precise 分代 83s 崩溃已破:终结器 poll 的嵌套完整回收)

接上节,gdb 单 run 系列(run A-J,LD_PRELOAD 空 .so 固定布局,env 增减实测
不影响本 repro 布局)逐层取证:

**现场修正**:前节对 victim 的解读是红鲱鱼——run A 把 parse_postfix 帧里的
合法 `emperor.Expression` 枚举局部(meta+tag)误读成了 at() 的暂存参数。真
victim 链:peek 的 this=TokenStream#10 **出生地址**(从未被晋升)、staged
List=tokens List 出生地址、idx=15124;崩溃算术 `slot_addr = 0 + 15124×64 =
0xEC500`(链表节点 64B)与故障地址精确吻合。List 对象尸检:meta/len=24312/
cap=32768 完好、**buf=0**——dispose_mem 的幂等清零签名,非内存复用覆写。

**时间线**:5 个 Parser(10 个 TokenStream)在 0.2-0.4s 构造完毕;此后
~83s 全部在文件 #5 的 parse 上(`_utils.List.at` 是 O(n) 走链 → O(n²) 解
析)。P5/stream/List 全部 mark=2(已终结尸体)而 parser 仍持有:34 个帧描述
符槽登记着 P5(Parser refmap 完整覆盖 stream@+8;TokenStream refmap 覆盖
tokens@+8——类型图无缺口)。

**击杀 BT(run H,对 List.buf 下 HW watchpoint 抓"非零后的首个清零")**:

```
List$Token.dispose_mem          ← buf=0 写入(List10)
_emperor_gc_finalize(List10)
gc_collect_generational :2718   ← 内层 major 的 old-pinned 终结遍历
gc_collect_main/auto ← _emperor_gc_poll ← 另一个 List.dispose_mem(企鹅终结器!)
_emperor_gc_finalize(h2)
gc_collect_generational :2718   ← 外层 major!
gc_collect_main/auto ← _emperor_gc_poll ← Parser.parse_postfix
```

**根因(双缺陷)**:
1. **重入门闩缺口**:`gc_collect_generational` 全程不持 `_emperor_gc_collecting`
   (只有 gc_minor 置位且返回即清)。major 的 old-pinned 终结 pass 运行企鹅
   dispose_mem,其内部 safepoint poll 畅通(`_emperor_gc_poll` 只查
   collecting)→ **嵌套完整 major** 在"半处理态"上开跑:外层 pass 已把早前
   chunk 的 survivor marked 复位为 0,嵌套 pass 重新审判把它们(连同 mark 未
   及到达的对象)**就地终结**——活 List 的 buf 被 dispose 释放清零,chunk 间
   mark_hit=1 只保 chunk 不回收、不保对象不被终结(run J:外层游标 chunk[7],
   内层游标 chunk[15]=List10 chunk;P5 chunk[21] 未及访问,三个对象已全 0)。
   附带隐患:外层正在 finalize 的 h2 在 dispose 运行期间仍 marked=0(`marked=2`
   在 finalize 返回后才置),嵌套 sweep 本可把"正在被执行终结器"的对象
   free 掉。
2. run I 分阶段仪器化(每 major 的 entry/pre-mark/post-mark/pass-start 四点
   采样)证明:非嵌套 major 的 mark 每次都正确(P5/ST/L10 post-mark 恒 1)。
   即 mark 机器无恙,kill 只能来自嵌套重审判。

**修复(gc.c,runtime-only,.ll 不变;三模式四产物 md5 逐字节一致 8e633110…)**:
1. `gc_collect_generational` 在其 gc_minor 返回后置 `_emperor_gc_collecting=1`
   直到所有出口(refresh 失败/mark_failed/正常尾)清零——终结器内的 poll 从
   此被 poll 入口既有检查拦截,"回收中不回收"对分代路径成立(与 sequential
   路径对齐;被拦的 want 标志保留,由 major 结束后的下一个 poll 消费)。
2. old-pinned 终结 pass(含 NO_CHUNK_RECLAIM 调试分支)改为**先置 marked=2
   再跑终结器**——dispose 执行期间对象即已读作 finalized-dead,任何路径都
   不可能对其二次终结或 sweep。

**验证**:precise 分代 repro 史上首次全绿(rc=0,≈1039s——慢但正确:phase B
老年代全量回退扫描 + 首次存活晋升的保留策略,性能课题归 3b-2 barrier/
3c 优化);conservative 132s / NOGEN 141s / default 141s 全绿;ASan torture
18 组合矩阵(模式×NOGEN×NOPROMOTE×NOCOVER×STRESS×GC_VERIFY×回收开关)
全 PASS;md BabyPenguin 342/342;precise ×10 复跑 + make bootstrap + md 全
矩阵见 states.txt 执行记录。precise 模式解除 experimental 标记的剩余条件:
性能(≈7.5× conservative)与生产 soak。
