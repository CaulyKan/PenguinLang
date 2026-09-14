# GC 代际陈旧墓碑（stale tombstone）—— json/hashmap pass3 损坏家族（2026-09-14 修复）

症状入口：IStringOps 会话发现 pass3 编译 json+hashmap+vector 的 meta 测试
（MetaJsonContainers / MetaJsonVectorOfSerializable）约 5-30% 概率挂：abort
`CORRUPT REF-MAP (slot offset outside object) type emperor.BoundTypeArg` /
SIGSEGV / 偶发语义错（绑定期读到损坏状态）。**非 IStringOps 因果**——它只是
把 stdlib 撑大、重掷了分配形态的骰子；漏洞自代际 GC（8e7cb559）起就在。

## 根因（完整取证链，gdb post-mortem + 运行时插桩双路确认）

**`gc_resolve_any` / `_emperor_gc_mark_object` / `gc_evacuate_slot` /
`gc_refmap_walk` 的墓碑转发（`yh->next`）无条件信任转发目标。**

时间线：
1. nursery 里的对象（取证实例：4272 字节的 string）在某次 minor 被 promote，
   留下墓碑（`next=新地址, marked=1`），chunk 因其它 pinned 幸存者被 demote
   （页保留，墓碑永久存在）。
2. 副本在后续 major 中死亡（合法）→ free → **malloc 复用该地址**
   （实测复用为 Vector<BoundTypeArg> 的元素缓冲，24 字节内联枚文
   {meta,tag,payload} 连续排列）。
3. 某容器 ref 槽 / 保守字持有指向**原对象体内**的内部地址（枚举 payload
   别名家族——不可重写，代码注释明言合法），或值恰好落入 nursery 区间的字。
4. 下个 major 的 region/保守扫描 resolve 它：chunk 命中 → owner=墓碑 →
   `return yh->next` → **把复用内存当对象基址返回** → mark_drain 用
   `h->size`（实为元素 A 的 payload 指针低 32 位，恰为负数）走
   BoundTypeArg 的 refmap → node 5 slot 越界 abort；body[0] 是标量（0x51/
   0x31）时直接 SEGV 在 `meta->refmap`（offset 0x40）。
5. 语义错变体 = 编译器通过陈旧地址读到复用内存内容。

**开关矩阵全部吻合**：NOGEN=1 干净（不动）、GC_DISABLE 干净（不收）、
STACK_COVER=1 干净 30/30（保守 cover 每 minor 钉住 SSA 拷贝目标——另一层
保护）、REGION_PINS 弱干净（更少 promote）。GC_VERIFY/gdb attach 被 时序
扰动掩蔽（Heisenbug）。

## 修复（gc.c，EmperorPenguin/std/c/gc.c）

`gc_forward_target_valid(tgt)`：目标必须是真块——sorted index 精确命中
（`gc_resolve_block(tgt)==tgt`）或 pending 成员（本 collection 刚 promote，
index 未刷新；**用 `_gc_pending_old_hash` 镜像 O(1) 判**，线性数组只做
partial-mirror 兜底——否则 promotion 密集的 minor 是 O(n²)）。index 不可用
（stale/空）时放行（该周期本就全保留）。四处接入：
- `gc_resolve_any`：死目标 → 返回 NULL（不 mark）。
- `_emperor_gc_mark_object` nursery 分支：死目标 → 不递归。
- `gc_evacuate_slot` forwarded 分支：死目标 → `*slot = NULL`（不把死指针写进槽）。
- `gc_refmap_walk` mark 路径：死目标 → owner=NULL。

语义正当性：指向已死对象内部地址的保守字本来就该被忽略；demoted chunk 的
冻结字节继续服务通过**旧地址**的读（语义安全），唯一错误是把**死目标**当
对象走查。轻量诊断保留：`gc_refmap_abort` 带 `_gc_walk_ctx`（walk 来源标
签）+ slot-offset abort 时 dump root header/位置/map/body。

验证：复现循环 60/60 绿（修复前合并失败率 ~5.4%）。

## 附带修复（同 commit，emitter）

`LLVMEmitter.scan_gcpin_clears` 的消费者枚举不完整（e453de71 引入的 RSS
优化）：只认 `wrmbr.obj/rdmbr.obj/rdenum/assign.src/unbox/call/call_void/
call_virt.obj`，漏了 `wrmbr.value`、`call_virt.args`、`call_indirect.
callee+args`、`binop`（字符串拼接）、`cast`、`ret`、`new/new_enum`、`box`、
`isinstance/isenum`、`global_store`、`address_of/load_ptr/store_ptr`——
mirror 被提前置 null 后 SSA 拷贝还在用，poll 窗口内目标可被 promote 走。
重构为 `gcpin_use_operands(inst)` 完整枚举（新增指令类型必须同步扩展——
clear 的安全性 = 枚举的完备性）。独立真实洞（未改变本 bug 复现率，但同族
暴露面）。

## 调试 playbook（这次沉淀的）

- **gc.c-only 改动的快速迭代**：`EmperorPenguin/emperor link build/bootstrap/pass3.ll -o /tmp/x/pass3dbg -enable-meta`——不重发 .ll，秒级~分钟级重链（std/c 按 OUTPUT_DIR 重编）。emitter/.penguin 改动才需要 `make build/bootstrap/pass3` 全链（当前 ~2 分钟）。
- **systemd-coredump 的 core 是截断的**（GB 级进程只 dump 几十 MB，部分 .bss 缺失）——post-mortem 查全局变量会假失败；线程栈通常在。
- **Heisenbug 对策**：插桩越重越不触发。有效路径是"在后果处守卫引爆"——
  gc_mark_drain 弹出时验证块真实性（nursery owner 或 index 成员），非法就
  abort 并 dump push 来源环形缓冲；再在嫌疑路径（墓碑转发）加目标验证打
  印。guard 命中率不受时序影响（它检查的是谓词不是时序）。
- **abort 消息带 walk 来源标签**（_gc_walk_ctx：mark-drain/typed-major/
  evac-body/frame-*/typed-*）——下次任何 refmap abort 直接知道是哪个收集
  阶段在走。
