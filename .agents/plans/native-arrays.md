# 原生数组 `T[]` + 内存内在函数退役 + GC 精确化（第一轮）

状态：已批准（2026-10-16），待实现。分支名：`feature/native-arrays`，每里程碑一次提交。

## 背景与动机

`#__store`/`#__load` 等裸内存内在函数权限过大、易被滥用，其存在的唯一理由是实现数组。
本轮给 PenguinLang 增加真正的数组类型 `T[]`，随即退役这套内在函数，并借"编译器知道数组类型"简化 GC。
后续两轮（不在本计划内）：删 `unsafe_cast`（元引擎对象桥句柄化）；保守栈扫描退役（C 运行时 rooting + safepoint 精确化 + GC_VERIFY 长期证据，即 gc.c 注释里的 phase 3）。

## 已确认的设计决策

- `T[]` 从「`mut IIterator<T>` 语法糖」改为**真正的密集数组类型**（引用类型、GC 对象、创建后长度不可变；可嵌套 `T[][]`；裸 `T[]` 默认可变）
- **不加** `a[i]` 索引语法、**不做**数组字面量（后续单独加）；元素访问走魔法方法 `at(i) -> Option<T>`（越界 none）/ `set(i, v)`（越界无操作，需 mut 绑定）/ `size() -> u64` / `iter()`/`iter_mut()`
- 构造：`create_array<T>(len, fill) -> mut T[]` 与 `create_array_with<T>(len, f: fun(u64)->T)`；无空引用——引用元素必须给初始值
- **彻底删除** `#__load`/`#__store`/`#__track_buffer`/`#__address_of` 全链路 + `__builtin._malloc/_mfree/_gc_scan_add/_gc_scan_remove` 的 penguin 表面；保留 `#sizeof`
- **GC 激进简化**：删 typed region 整套 + **删 IMemoryDispose/finalize 整套**（destructor 槽、`_emperor_gc_finalize`、sweep finalize）；保留 quarantine、meta pin、保守栈扫描、保守 region 注册表（scheduler 依赖）
- `std.io.File` 去自动关闭：显式 `close()`，不关则句柄泄漏到进程退出
- `unsafe_cast` 本轮不动（下一轮连同元引擎对象桥句柄化单独删）
- 删 `std.Array<T,N>`；`_utils.List`/`std.Vector` 底层改原生数组，公开 API 不变（调用者零改动）

## PenguinIR 变动（双侧同一套，display() 字节一致）

doc 22 声明的共享契约：同一指令集、同一值模型、`display()` 文本字节一致。
**净效果：EP 30→31 变体（−3 指针 +4 数组），BP 27→31；共享指令 25→29，doc 22 分叉清单里最大的一条（裸指针）清零。**

新增 4 条薄正交指令（语义：值类元素 = 整结构拷贝）：

```
%a:T[] = ARRAY_NEW_ZEROED <T> %len
%v:T   = ARRAY_GET %arr, %idx
ARRAY_SET %arr, %idx, %v
%n:u64 = ARRAY_LEN %arr
```

- `ARRAY_NEW_ZEROED` 由 EP 侧降为对 `_emperor_array_create(meta, count, stride)` 的调用（helper 内分配后即刻清零，回收槽陈旧数据防护；分配瞬间 poll 窗口由 quarantine 兜底——已确认现机制覆盖）
- `__array_fill(a, v)` / `__array_copy_range(dst, src, n)` 走普通 C extern 调用（数组按 ptr 传参），不占新指令
- Option 包装/边界检查/create_array(_with)/ArrayIterator/iter() 全部是**一份镜像 penguin 源**（`core_builtin.penguin` ↔ `BabyPenguin/Builtin.penguin`，IStringOps 先例）——语义只写一遍，BP VM / BP cs 后端 / EP 行为天然一致
- 编译器按名识别 6 个 `__builtin.__array_*` 内置函数（IRGenerator 名字派发，复用原 `<intrinsic>.*` 机制）；`a.at(i)` 等魔法方法在绑定层重写为对这些函数的调用
- 双侧落地：BP `IRInstruction.cs`/`IRGenerator.cs`/`RuntimeFrame.cs`/`IRModule.Display`；EP `IRInstruction.penguin`/`IRBuilder.penguin`/`IRGenerator.penguin`/`IRPrinter.penguin`
- doc 22 更新：指令清单、删分叉#2（裸指针）、**补记 GC_POLL 分叉**（现行清单漏记）
- 保留的既有分叉不动：间接调用形状（BP 拆 CALL_FUNC_PTR±Void / EP 合 CALL_INDIRECT）、BP RET 携带 ReturnStatus、CALL_VIRT/BOX/UNBOX 在 BP VM 未实现

## 数组对象设计

- 布局 `{ ptr metadata, i64 length, elements... }`（16B 头 + n×stride）
- 每元素类型一个 metadata 全局（name="T[]"，instance_size=0=变长），refmap 用**已预留未用的 REFARR 节点（kind 3，emperor_types.h:37）**：walker 从对象 +8 读 length、按 stride 逐元素走 elem 节点（primitive 元素 = 精确 no-op，不再保守回退）
- span 槽 ≤512B / malloc 大对象路径照常
- 内部原语 `__array_alloc_zeroed` 供 Vector/List 扩容 spare 槽（零对追踪惰性安全，spare 槽永不读）

## 里程碑

### M1 — BabyPenguin（测试回路最快；必须先于 EP——pass1 用 BP 编译 EP 源）

- `BabyPenguin/Parser/SyntaxNodes/TypeSpecifier.cs:82` 删 IIterator 重写；`Common.cs` 加 `TypeEnum.Array` + `ArrayType`；`ResolveType` 数组缓存
- `IRInstruction.cs` 4 条新指令 + `IRGenerator.cs` 内置名派发 + `RuntimeFrame.cs` 解释（`ArrayRuntimeValue`）+ Display
- `Builtin.penguin`：4 个 `__array_*` 声明 + `__array_at/set/len/iter` + `create_array(_with)` + `ArrayIterator<T>`；for-in 加数组分支（ICodeContainer.cs:502 合成 `.iter()`，复用现有三分支）
- C# 后端遇数组明确报错（pass1 源集不含数组，自举不受影响；BabyPenguin CLI 默认后端是 vm），完整支持列为后续
- `DeclarationTests.cs:1048 IteratorDefinition` 更新；`Tests/ArrayTest/*.md` 先跑 babypenguin

### M2 — EmperorPenguin 原生数组

- `TypeKind.ArrayKind` + registry `array_type_of(elem)`；`SemanticModel.penguin:811-841` 替换 IIterator 重写（顺手修泛型实参丢失 caveat：行 821-827 只取 name+qualified_parts）；monomorphize 元素替换；`#sizeof(T[])=8`
- IR 4 指令（`IRInstruction.penguin`/`IRBuilder`/`IRGenerator`/`IRPrinter`，display 与 BP 字节一致）；LLVMEmitter：布局类型、metadata+REFARR refmap、`_emperor_array_create` 调用、值类元素 memcpy（复用原 emit_load_ptr/emit_store_ptr 4401-4449 的逻辑）
- Formatter（EP `src/ast/Formatter.penguin`）数组类型打印

### M3 — C 运行时与 GC 简化

- gc.c：REFARR walker（`gc_refmap_walk` 1003-1067 扩展）；删 typed region（GCScanRegion 的 elem_count/stride/map 331-337、`_emperor_gc_track_buffer` 496-521、`gc_scan_regions_mark` typed 分支 1284-1294、注册清零 hack 500-510）；删 finalize 整套（`_emperor_gc_finalize` 1181-1189、sweep 两阶段 1197-1254、`gt_sweep_finalize`）
- core_builtin.c：`_emperor_array_create`/`_emperor_array_fill`/`_emperor_array_copy_range`；删 penguin 表面 extern
- `std.io.File` 去 IMemoryDispose；gc_torture.c 用例改写（461/495/609/621 的 track_buffer 用例→原生数组对象拷打、finalize 用例删）；scheduler.c 不动（只用保守 region）

### M4 — 容器迁移 + 删内在函数 + 自举

- `src/utils.penguin` List 底层改 `mut T[]`（`__array_alloc_zeroed`+`__array_copy_range` 扩容，_grow 101-116 重写，API 不变；_ListIterator.next 278 改 at）；`std/penguin/vector.penguin` 同样重构；删 `array.penguin`
- 删 `#__load/#__store/#__track_buffer/#__address_of`：`SemanticBindMetaCalls.penguin` 20-73/486-615、`IRGenerator.penguin` 696-758、`LLVMEmitter.penguin` 3578-3580/4383-4789、IR 三指令（ADDRESS_OF/LOAD_PTR/STORE_PTR）
- `make bootstrap` 全链 + pass5 md5 收敛验证

### M5 — 测试、文档、发布验证

- 迁移：`Tests/AsyncTest/YieldBeforeRewriteTest.md:12`、`Tests/BuiltinTest/RangeTest2.md`（→ 显式 `mut IIterator<T>`）；`Tests/GcTest/GcNoStackCoverRegionPin.md`、`Tests/MetaProgramming/GenericMetaTypeArg.md`（→ 原生数组等价测试）；StdArray* 系列（StdArrayTest/ArraySetCallInFunction/StdArrayAutoDispose/StdArrayValueParam 换 value-param 载体）；**LSP Completion.md 巨型 JSON**（去掉 _malloc/_mfree/_gc_scan_*/IMemoryDispose/dispose_mem 关键字）
- 新测试电池 `Tests/ArrayTest/`：构造/读写/嵌套/ref 元素 GC 存活/值类元素拷贝/string[]/create_array_with/泛型中的数组/类字段数组 GC 追踪/迭代器互操作/dynlib 边界，按验证结果逐步扩大 Apply To
- 文档双语同步：22_IR（重点）、03_DataTypes、25_AST、27_LLVM、29_GC、31_MetaProgramming + CLAUDE.md/AGENTS.md（stdlib 表、C runtime 表、内在函数清单）
- `make all` 全绿 + `make gc-bench` 对比 + **release_win 交叉验证**（改 C 运行时必验）+ `make tools-test`（Formatter）

## 调研事实存档（实现时免重查）

- `T[]` 现状：grammar `iterableType: '[]'`（PenguinLang.g4:143）；BP 在 `TypeSpecifier.Name` 字符串级重写为 `mut __builtin.IIterator<T>`；EP 在 `resolve_type_specifier`（SemanticModel.penguin:811-841）语义级重写。生产代码零用户，仅 2 个 md 测试 + DeclarationTests.IteratorDefinition
- EP 词法无单独 `[` token（只有融合 ArrayBracket，Lexer.penguin:689-696）；本轮不加索引语法，故词法/语法零改动
- 内在函数用户全表：`src/utils.penguin`（List/_ListIterator）、`std/penguin/array.penguin`、`std/penguin/vector.penguin`、2 个 md 测试；`#__address_of` 零用户；hashmap.penguin 不用（建于 Vector 之上）
- `_utils.List` 双轨制：pass1 在 BP VM 上用 C# extern（BabyPenguin/Utils.penguin:59 → ExternFunctions.cs AddList）；pass2+ 编译 `src/utils.penguin` 的裸缓冲实现。本轮只改 penguin 侧实现，C# extern 原样
- scheduler.c 只用保守 scan_add/set_live（809-948 多处），不用 track_buffer → typed region 可整体删除
- GC 元数据 EmperorClassMetadata（emperor_types.h:44-57）：name/instance_size/field_count/field_offsets/field_is_ptr/vtable/interface_map/destructor/refmap；marker 只消费 refmap（+destructor/name/instance_size）
- BabyPenguin CLI 默认后端 vm（Program.cs:23）；--backend=cs 仅 bootstrap pass1（Makefile:264-270），pass1 源集（EmperorPenguinPass1.penguins）不含 src/utils.penguin 与数组语法
- create_array 目前不存在；唯一裸分配原语 `__builtin._malloc/_mfree`（libc malloc，GC 域外）
- doc 22（impl-notes/22_PenguinLangIR.md）是共享 IR 契约文档，已知分叉 4 条 + 漏记 GC_POLL

## 风险与缓解

- 自举收敛：List 重写确定性高，pass5 应收敛；不收敛则排查非确定来源
- 自举性能：边界检查+16B 头开销 → 对比 bootstrap 耗时，超标再加批量原语（`__array_copy_range` 已缓解大头）
- 四后端一致性：语义集中在一份镜像 penguin 源 + IR display 字节一致契约
- C# 后端：明确报错不阻塞自举
- GC 窗口安全：回收槽清零在 helper 内即刻完成；分配瞬间 poll 由 quarantine 兜底（512 环/arena）
