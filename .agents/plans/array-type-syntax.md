# `T[]` → `array<T>` 语法替换（彻底移除后缀拼写）

状态：已完成（2026-10-17，提交 986c3e37）。分支：`feature/native-arrays`。遗留：ArrayNestedChainThenIterate 红哨兵（既有链式读内存踩踏，与本轮无关）；ArrayIterator 显式注解创建 layout-only 特化的既有问题。

## 动机

`mut i32[]` 的 `mut` 前缀语法上说不清修饰数组还是修饰元素（现行语义：整体归数组、
元素一律不可变——拼写本身没有表达这一点）。改为 `array<T>` 后两层各归其位：
`mut array<i32>` = 可写数组引用；`array<mut i32>` = 元素可变（本轮**不实现**，
明确报错，避免静默归一化的陷阱）。**`T[]` 后缀语法从语法层彻底删除**，杜绝双拼写并存。

## 设计决策

- `array` 是保留的类型构造器名（像 primitive 一样在解析层拦截，不可被用户类遮蔽——
  Box 遮蔽教训）。裸 `array`（无泛型实参）→ 报错"array 需要元素类型"。
- 解析零改动：`array<i32>` 走现有 identifierWithDotsAndGenericArguments 泛型路径，
  拦截发生在类型解析层（EP resolve_type_specifier / BP ResolveTypeNodeUncached）。
- **内部拼写同步替换**：display_name / serialize_bound_type / IR 类型串全部从
  `i32[]` 改为 `array<i32>`（嵌套 `array<array<i32>>`）。mangle 哈希随之变化——
  全套重建本来就在本任务内，一次到位，不留 `[]` 痕迹。
- 元素显式可变性（`array<mut i32>` / `array<!mut i32>`）→ 明确报错
  "array element mutability is not supported yet"，双侧一致。
- IR 指令名（ARRAY_*）、`__builtin.__array_*`、GC REFARR、VM ArrayRuntimeValue
  不动——这些没有 `[]` 拼写。
- std.Array 已在 M3/M4 删除，无需改名；未来定长数组叫 FixedArray<T,N>。

## 里程碑（顺序敏感：BP 先行，pass1 在 BP VM 上跑）

### M1 — BabyPenguin（C#）
- PenguinLang.g4：删 `iterableType: '[]'` 规则与 typeSpecifier 引用（ANTLR 构建期再生成）
- TypeSpecifier.cs：删 IsIterable/IterableElementName/Name 折叠 hack（Name 走普通
  泛型路径，反而变简单）
- SemanticModel.cs:436：`EndsWith("[]")` 分支 → `array<...>` 包装分支（剥壳递归）
- ArrayTypeNode 显示、IRTypeClassifier.cs、IRGenerator.cs 的 ir 类型串、
  LambdaFunctionExpression/FunctionDefinition 的类型文本 → array<T> 拼写
- Builtin.penguin 镜像源 13 处注解清扫
- dotnet 测试期望文本（DeclarationTests 等）同步
- 验证：BP VM 跑 ArrayTest 全绿

### M2 — EmperorPenguin
- resolve_type_specifier：is_iterable 分支 → array 名拦截 + 元素可变性拒绝
- 删 is_iterable 链：AST.penguin 字段、Parser.penguin、Lexer.penguin ArrayBracket、
  SemanticBindExpressions 推断 unify、SemanticBuildScopes 消费点
- BoundType.penguin serialize/display、IRGenerator bound_type_to_ir_type、
  LLVMEmitter ir_type_to_llvm（ends_with "[]" → 前缀 array<）、Formatter 数组打印
  （可能直接退化为普通泛型打印）
- core_builtin.penguin 19 处清扫
- 验证：pass1 快速回路（BP VM 编译 EP 源 + core_builtin），ArrayTest probe

### M3 — 全量清扫
- utils.penguin(3)/vector.penguin(4) 注解；12 个测试 md（sed：`mut X[]`→`mut array<X>`，
  嵌套从内向外迭代）；文档 03/22/27/29 + 教程 + AGENTS.md/CLAUDE.md + Tests/Readme；
  新增负测试：裸 array、array<mut i32> 拒绝
- C# 后端的数组报错文案

### M4 — 重建与验证
- make bootstrap（pass5 收敛）；release/lsp/tools；全量矩阵；dotnet test；
  gc-bench；tools-test；LSP 快照按需重录；提交

## 风险

- BP 字符串类型系统里 "array<i32>" 与泛型类名字形状相同——拦截必须先于作用域查找
- mangle 哈希变化 → std dynlib、编译器 lib 全重建（本就在任务内）
- dotnet 测试期望文本散落多处（ASTBuildTextTest 等），grep T[] 逐个核对
