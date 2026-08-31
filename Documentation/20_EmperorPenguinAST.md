# 20. EmperorPenguin AST

## Overview

EmperorPenguin 的 AST（抽象语法树）是编译器前端解析阶段的输出。源文件经过 Lexer 分词、Parser 解析后生成 `CompilationUnit`，其中包含顶层 `Definition` 列表。

**源文件位置**: `EmperorPenguin/src/ast/`

**核心文件**:
- `Token.penguin` — TokenType 枚举、Token 与 TokenStream
- `Lexer.penguin` — 词法分析器（源码 → Token 流）
- `Parser.penguin` — 语法分析器（Token 流 → CompilationUnit）
- `AST.penguin` — 所有 AST 节点定义（表达式、语句、定义）
- `SourceLocation.penguin` — 源位置（filename / line / col）

---

## Token (parser namespace)

### TokenType 枚举

PenguinLang 的全部词法单元类型：

| 分类 | Token 类型 |
|------|-----------|
| 字面量 | `EOF`, `Identifier`, `Constant`, `Digitsequence`, `Stringliteral` |
| 内部 | `InternalBlocked_return`, `InternalSignal`, `InternalYield_finished_return`, `InternalYield_not_finished_return` |
| 运算符 | `Minus`/`MinusAssign`, `Arrow`, `Bang`/`BangEqual`, `NotAsync`, `NotMut`, `NotPure`, `Star`/`StarAssign`, `Slash`/`SlashAssign`, `Percent`/`PercentAssign`, `Ampersand`/`AmpersandAmpersand`/`AmpersandAssign`, `Caret`/`CaretAssign`, `Plus`/`PlusAssign`, `Less`/`LessEqual`, `Assign`, `ColonAssign`, `EqualEqual`, `Greater`/`GreaterEqual`, `Pipe`/`PipeAssign`/`PipePipe`, `Tilde` |
| 分隔符 | `Comma`, `Semicolon`, `Colon`, `Dot`, `Ellipsis`, `LParen`, `RParen`, `ArrayBracket`（`[]`）, `LBrace`, `RBrace`, `Hash` |
| 关键字 | `Async`, `Async_fun`, `AutoKw`, `Bool`, `Break`, `Cast`, `UnsafeCast`, `Char`, `Class`, `Continue`, `Double`, `Else`, `ElifKw`, `Enum`, `Extern`, `Export`, `False`, `Float`, `For`, `Fun`, `I8`-`I64`, `U8`-`U64`, `If`, `Impl`, `In`, `Initial`, `Interface`, `Is`, `Let`, `Mut`, `Namespace`, `New`, `Pure`, `Return`, `SelfKw`, `StringKw`, `TemplateKw`, `ThisKw`, `True`, `TypeKw`, `Using`, `Void`, `Wait`, `While`, `Yield` |
| 端口/并发词汇 | `Connect`, `ConstructKw`, `Tick`, `TryKw`, `CatchKw`, `Input`, `Output` |

### Token 类

```
Token
├── token_type: TokenType       # 词法单元类型
├── text: string                # 原始文本
└── location: SourceLocation    # 源位置（filename + line + col）
```

### TokenStream 类

Token 流，支持向前看和消费操作：

```
TokenStream
├── tokens: List<Token>       # Token 列表
└── pos: mut i64              # 当前位置
```

| 方法 | 说明 |
|------|------|
| `peek() -> Token` | 查看当前 Token |
| `peek_type() -> TokenType` | 查看当前 Token 类型 |
| `advance() -> Token` | 消费并返回当前 Token |
| `expect(expected) -> Token` | 消费并验证当前 Token 类型 |
| `match(expected) -> bool` | 若匹配则消费 |
| `is_at_end() -> bool` | 是否到达末尾 |

---

## AST 节点 (ast namespace)

所有 AST 节点提供 `build_text()` 方法；表达式、语句、定义节点分别实现 `IExpression`、`IStatement`、`IDefinition` 接口。

### 运算符枚举

**BinaryOperator**:
`Add` | `Subtract` | `Multiply` | `Divide` | `Modulo` | `LessThan` | `GreaterThan` | `LessThanOrEqual` | `GreaterThanOrEqual` | `Equal` | `NotEqual` | `LogicalAnd` | `LogicalOr` | `BitwiseAnd` | `BitwiseOr` | `BitwiseXor` | `Is`

**UnaryOperator**: `Negate` | `Not` | `BitwiseNot` | `UnaryPlus` | `Deref` | `Ref`

**AssignmentOperator**: `Assign` | `PlusAssign` | `MinusAssign` | `StarAssign` | `SlashAssign` | `PercentAssign` | `AmpersandAssign` | `CaretAssign` | `PipeAssign`

---

### 表达式 (Expression 枚举)

| 变体 | 类名 | 字段 | 说明 |
|------|------|------|------|
| `constant` | ConstantExpression | `value: string` | 数字/字符常量 |
| `identifier` | IdentifierExpression | `name: string`, `generic_args: List<TypeSpecifier>`, `value_generic_args: List<Expression>` | 标识符引用 |
| `string_literal` | StringLiteralExpression | `value: string` | 字符串字面量 |
| `bool_literal` | BoolLiteralExpression | `value: string` | 布尔字面量 |
| `parenthesized` | ParenthesizedExpression | `inner: Option<Expression>` | 括号表达式 |
| `binary` | BinaryExpression | `operators: List<BinaryOperator>`, `operands: List<Expression>` | 二元运算（支持链式） |
| `unary` | UnaryExpression | `operator_value: UnaryOperator`, `operand: Option<Expression>` | 一元运算 |
| `member_access` | MemberAccessExpression | `base_expr: Option<Expression>`, `member_name: string`, `generic_args: List<TypeSpecifier>` | 成员访问 `a.b` |
| `function_call` | FunctionCallExpression | `callee: Option<Expression>`, `arguments: FunctionCallArguments` | 函数调用 |
| `function_call_arguments` | FunctionCallArguments | `items: List<Expression>` | 函数调用实参列表（独立节点，供元引擎解析） |
| `if_expr` | IfExpression | `condition`, `then_block`, `else_block` (all `Option<Expression>`) | if 表达式 |
| `while_expr` | WhileExpression | `condition`, `body` (all `Option<Expression>`) | while 表达式 |
| `code_block` | CodeBlockExpression | `statements: List<Statement>`, `trailing_expr: Option<Expression>` | 代码块 |
| `cast_expr` | CastExpression | `type_name: string`, `target_type: Option<TypeSpecifier>`, `inner: Option<Expression>`, `is_unsafe: bool` | 类型转换（`is_unsafe` 为 `unsafe_cast`） |
| `new_expr` | NewExpression | `type_name: string`, `generic_args: List<TypeSpecifier>`, `arguments: List<Expression>` | new 表达式 |
| `lambda_expr` | LambdaFunctionExpression | `parameters`, `return_type`, `body`, `is_async` | lambda 函数 |
| `spawn_async` | SpawnAsyncExpression | `expression: Option<Expression>` | async 启动 |
| `wait_expr` | WaitExpression | `expression: Option<Expression>`, `is_tick_unit: bool` | wait 表达式 |
| `void_literal` | VoidLiteralExpression | `value: string` | void 字面量 |
| `meta_call` | MetaCallExpression | `func_name: string`, `arguments: List<Expression>`, `trailing_block_raw: Option<string>`, `trailing_definition: Option<Definition>` | `#fun(...)` 元调用 |
| `specializing_impl` | SpecializingImplExpression | `impl_def: Option<InterfaceImplementation>`, `slot: i64` | `#specializing` 块内的条件 impl 语句 |
| `try_bind` | TryBindExpression | `variable_name: string`, `type_spec: Option<TypeSpecifier>`, `rhs: Option<Expression>` | `let a [: T] := b` try-bind |

---

### 语句 (Statement 枚举)

| 变体 | 类名 | 字段 | 说明 |
|------|------|------|------|
| `expression` | ExpressionStatement | `expression: Option<Expression>` | 表达式语句 |
| `block_expr` | BlockExpressionStatement | `expression: Option<Expression>` | 块表达式语句 |
| `assignment` | AssignmentStatement | `target`, `operator_value`, `value` | 赋值语句 |
| `if_stmt` | IfStatement | `condition`, `then_statement`, `else_statement` | if 语句 |
| `while_stmt` | WhileStatement | `condition`, `body` | while 语句 |
| `for_stmt` | ForStatement | `variable_name`, `variable_type`/`type_spec`, `is_mutable`, `iterable`, `body` | for 循环 |
| `return_stmt` | ReturnStatement | `value: Option<Expression>` | return 语句 |
| `break_stmt` | BreakStatement | `value: Option<Expression>` | break 语句 |
| `continue_stmt` | ContinueStatement | — | continue 语句 |
| `let_decl` | LetDeclarationStatement | `is_mutable`, `variable_name`, `variable_type`/`type_spec`, `initializer` | let 声明 |
| `connect_stmt` | ConnectStatement | `source`, `sink` | `connect(source, sink);`（仅 construct 块内合法） |
| `try_stmt` | TryStatement | `try_block`, `catch_var_name`, `catch_type_spec`, `catch_block` | try/catch 语句 |
| `yield_stmt` | YieldStatement | `value: Option<Expression>` | yield 语句 |
| `signal_stmt` | SignalStatement | `expression: Option<Expression>` | signal 语句 |
| `meta_if_stmt` | MetaIfStatement | `condition`, `then_statement`, `elif_branches`, `else_statement` | `#if` 编译期语句 |
| `meta_while_stmt` | MetaWhileStatement | `condition`, `body` | `#while` 编译期语句 |
| `meta_for_stmt` | MetaForStatement | `variable_name`, `variable_type`, `is_mutable`, `iterable`, `body` | `#for` 编译期语句 |
| `meta_break_stmt` | MetaBreakStatement | `value: Option<Expression>` | `#break` 编译期语句 |
| `meta_continue_stmt` | MetaContinueStatement | — | `#continue` 编译期语句 |

---

### 定义 (Definition 枚举)

| 变体 | 类名 | 关键字段 | 说明 |
|------|------|---------|------|
| `function_def` | FunctionDefinition | `name`, `parameters`, `return_type`, `body`, `is_extern`/`is_pure`/`is_async`/`is_new`, `template_decl` | 函数定义 |
| `class_def` | ClassDefinition | `name`, `members: List<Definition>`, `template_decl` | 类定义 |
| `enum_def` | EnumDefinition | `name`, `members: List<Definition>`, `template_decl` | 枚举定义 |
| `namespace_def` | NamespaceDefinition | `name`, `children: List<Definition>` | 命名空间 |
| `using_def` | UsingDefinition | `name` | `using <ns>;` 指令 |
| `initial_routine` | InitialRoutineDefinition | `body: Option<Expression>` | initial 块 |
| `interface_def` | InterfaceDefinition | `name`, `members`, `template_decl` | 接口定义 |
| `impl_def` | InterfaceImplementation | `type_name`, `type_spec`, `functions` | impl 块（类内） |
| `impl_for_def` | InterfaceForImplementation | `type_name`, `for_type_name`, `type_spec`, `for_type_spec`, `functions` | impl...for 块 |
| `port_def` | PortDefinition | `is_input`, `name`, `type_spec`, `default_expr` | 类内 `input`/`output` 端口声明 |
| `construct_def` | ConstructDefinition | `body: Option<Expression>` | `construct { ... }` 精化块（connect 唯一合法作用域） |
| `type_ref_def` | TypeReferenceDefinition | `name`, `type_spec` | 类型别名 |
| `enum_member` | EnumMemberDefinition | `name`, `type_spec` | 枚举成员 |
| `class_field` | ClassFieldDefinition | `name`, `mutability`, `type_spec`, `initializer` | 类字段 |
| `global_var` | GlobalVariableDefinition | `name`, `type_spec`, `initializer`, `is_mutable` | 顶层全局变量 |
| `meta_function_def` | MetaFunctionDefinition | `name`, `parameters: List<MetaParameter>`, `return_type`, `body` | `#fun` 定义 |
| `meta_class_def` | ClassDefinition | `name`, `members` | `#class` 元数据类定义 |
| `meta_if_def` | MetaIfDefinition | `condition`, `then_definitions`, `elif_branches`, `else_definitions` | `#if` 定义块 |
| `meta_for_def` | MetaForDefinition | `variable_name`, `iterable`, `body: List<Definition>` | `#for` 定义块 |
| `meta_while_def` | MetaWhileDefinition | `condition`, `body: List<Definition>` | `#while` 定义块 |
| `meta_call_def` | MetaCallDefinition | `call: Option<Expression>` | 顶层 `#fun(...)` 调用 |
| `specializing_def` | SpecializingDefinition | `target_type_spec`, `body: List<Expression>`, `impl_fragments` | `#specializing` 条件 impl 块 |

---

### 辅助类型

**TypeSpecifier** — 类型描述符：
- `name: string` — 类型名
- `generic_args: List<TypeSpecifier>` — 泛型参数
- `is_mutable` / `is_not_mutable` / `is_auto_mutability` — 可变性标记
- `is_value_arg` / `value_arg_text` — 值（非类型）泛型参数（如 `A<5>` 中的 `5`）
- `is_iterable` — 数组/可迭代后缀 `[]`
- `is_function_type` / `is_async_function_type` — 函数类型标记
- `function_params` / `return_type` — 函数类型参数
- `qualified_parts: List<string>` — 限定名部分
- `meta_call: Option<MetaCallExpression>` — 类型位置的 `#fun()` 调用（返回 type）

**Parameter** — 参数定义：
- `name`, `type_spec`, `default_value`, `is_mutable`, `is_this`

**TemplateDeclaration** — 模板声明：
- `parameters: List<TemplateParameter>` (name + type_constraint + `is_value_param` 值参数标记)

---

### 编译单元

**CompilationUnit** — 顶层 AST 节点：
- `definitions: List<Definition>` — 所有顶层定义
- `build_text() -> string` — 重建源码文本

---

## Pipeline 位置

```
Source Code → Lexer (TokenStream) → Parser (CompilationUnit) → SemanticModel → Bound Tree → IR
```

AST 是纯语法结构的表示，不携带语义信息（类型、符号引用等）。语义分析在 `SemanticModel` 中将 AST 转换为 Bound Tree。
