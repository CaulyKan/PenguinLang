# 值类型 Enum 大小修复 (Option<T> 等)

## 问题
1. EmperorPenguin 中值类型 enum 的大小恒为 24（payload 恒按指针算），与实际
   LLVM struct `{ ptr, i32, payload }` 不符。当 T 为值类型时应为
   meta(8) + tag(4) + sizeof(T)（按 payload 对齐，总大小 align 8）。
2. enum 布局先于 class 布局构建 → 值类型 class payload / 后向引用的值类型
   class 字段全部退化为 `ptr`（`Option$...Node = {ptr,i32,ptr}`、
   `A{p:B} = {ptr,ptr}`），语义模型与发射布局错位。
3. 递归值类型（`impl IValueType` + `next: Option<Node>`）无循环检测，
   修复大小模型后会无限递归 — 必须报错"无法计算 Node 的大小"。
   （auto 分类的递归类按既有设计仍判为引用类型，链表语义保留。）
4. `#sizeof(Option<i32>)` 在无实例化时回退到未特化模板 → 24。
5. `_utils.List` 槽位存 `Option<T>`（24B 恒定）→ 改为存裸 T（对齐 std.Vector）。

## 修改
- `EmperorPenguin/src/bound/BoundType.penguin`
  - `byte_size()` 枚举分支: payload@align_up(12,align(P))，size=align_up(end,8)；
    值class/枚举递归入栈检测，环 → 返回 -1。
- `EmperorPenguin/src/bound/SemanticBindMetaCalls.penguin`
  - `resolve_intrinsic_boundtype_ge`: 特化符号未找到且实参全部具体(非占位
    dummy：ClassKind 且 class_def.scope 为 none)时，走
    `ensure_specialized_def` + `catch_up_def` 按需创建。
  - `bind_sizeof_intrinsic`: byte_size == -1 → E_SIZE_CYCLE 报错。
- `EmperorPenguin/src/project/ErrorCode.penguin` + `PenguinLangParser/ErrorCode.cs`
  - 新增 `E_SIZE_CYCLE`（两侧同步）。
- `EmperorPenguin/src/llvm/LLVMEmitter.penguin`
  - 布局构建改按需递归（enum↔class 互引皆可解析），`is_building` 标记 +
    `llvm_type_size` 环检测 → `error[E_SIZE_CYCLE]: cannot compute size of 'X'`。
  - 大小公式统一: `compute_enum_struct_size` / `llvm_type_size(%enum.X)` /
    DWARF payload offset 全部改为 tag 后 12 起、按 payload 对齐、总 align 8。
  - 内联 `%class.` payload 代码生成: rdenum 返回 payload 指针、rdmbr 枚举
    payload 路径直接用 payload_ptr 做嵌套字段基址、new_enum 内联存储。
  - `emit_load_ptr`/`emit_store_ptr`: `ref<X>` 且 X 为值类型 class →
    整结构 load/store（修 Vector<ValueClass>/List<T> 存地址的 bug）。
- `EmperorPenguin/src/utils.penguin`
  - `List<T>` 槽位 = 裸 T：`#sizeof(T)` 步长、`#__load/store(T)`，at/pop/
    next 返回时包 `Option.some`（内联 payload 后无堆装箱开销）。

## 验证
- pass1 手测 → `./penguin -b` 自举 → PenguinTestRunner 全量 + dotnet test。
- 新增 Tests: sizeof 值断言、递归 IValueType 报错、嵌套值 class 字段、
  enum 值class payload 读写、List<ValueClass> 行为。
