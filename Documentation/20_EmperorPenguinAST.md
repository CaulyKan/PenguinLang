# 20. EmperorPenguin AST

## Overview

EmperorPenguin 的 AST（抽象语法树）是编译器前端解析阶段的输出。源文件经过 Lexer 分词、Parser 解析后生成 `CompilationUnit`，其中包含顶层 `Definition` 列表。

**源文件位置**: `EmperorPenguin/src/ast/`

**核心文件**:
- `Token.penguin` — 词法单元类型与 Token 流
- `AST.penguin` — 所有 AST 节点定义（表达式、语句、定义）

---

## Token (parser namespace)

### TokenType 枚举

PenguinLang 的全部词法单元类型：

| 分类 | Token 类型 |
|------|-----------|
| 字面量 | `EOF`, `Identifier`, `Constant`, `Digitsequence`, `Stringliteral` |
| 内部 | `InternalBlocked_return`, `InternalSignal`, `InternalYield_finished_return`, `InternalYield_not_finished_return` |
| 运算符 | `Minus`/`MinusAssign`, `Arrow`, `Bang`/`BangEqual`, `NotAsync`, `NotMut`, `NotPure`, `Star`/`StarAssign`, `Slash`/`SlashAssign`, `Percent`/`PercentAssign`, `Ampersand`/`AmpersandAmpersand`/`AmpersandAssign`, `Caret`/`CaretAssign`, `Plus`/`PlusAssign`, `Less`/`LessEqual`, `Assign`, `EqualEqual`, `Greater`/`GreaterEqual`, `Pipe`/`PipeAssign`/`PipePipe`, `Tilde` |
| 分隔符 | `Comma`, `Semicolon`, `Colon`, `Dot`, `Ellipsis`, `LParen`, `RParen`, `ArrayBracket`, `LBrace`, `RBrace`, `Hash` |
| 关键字 | `Async`, `Bool`, `Break`, `Cast`, `Char`, `Class`, `Continue`, `Double`, `Else`, `Emit`, `Enum`, `EventKw`, `Extern`, `False`, `Float`, `For`, `Fun`, `I8`-`I64`, `U8`-`U64`, `If`, `Impl`, `In`, `Initial`, `Interface`, `Is`, `Let`, `Mut`, `Namespace`, `New`, `On`, `Pure`, `Return`, `SelfKw`, `StringKw`, `TemplateKw`, `ThisKw`, `True`, `TypeKw`, `Void`, `Wait`, `Where`, `While`, `Yield` |

### Token 类

```
Token
├── token_type: TokenType    # 词法单元类型
├── text: string              # 原始文本
├── line: i64                 # 行号
└── col: i64                  # 列号
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

所有 AST 节点实现 `IExpression`、`IStatement` 或 `IDefinition` 接口，提供 `build_text()` 方法。

### 运算符枚举

**BinaryOperator**:
`Add` | `Subtract` | `Multiply` | `Divide` | `Modulo` | `LessThan` | `GreaterThan` | `LessThanOrEqual` | `GreaterThanOrEqual` | `Equal` | `NotEqual` | `LogicalAnd` | `LogicalOr` | `BitwiseAnd` | `BitwiseOr` | `BitwiseXor` | `Is`

**UnaryOperator**: `Negate` | `Not` | `BitwiseNot` | `UnaryPlus` | `Deref` | `Ref`

**AssignmentOperator**: `Assign` | `PlusAssign` | `MinusAssign` | `StarAssign` | `SlashAssign` | `PercentAssign` | `AmpersandAssign` | `CaretAssign` | `PipeAssign`

---

### 表达式 (Expression 枚举)

| 变体 | 类名 | 字段 | 说明 |
|------|------|------|------|
| `constant` | ConstantExpression | `value: string` | 数字常量 |
| `identifier` | IdentifierExpression | `name: string`, `generic_args: List<TypeSpecifier>` | 标识符引用 |
| `string_literal` | StringLiteralExpression | `value: string` | 字符串字面量 |
| `bool_literal` | BoolLiteralExpression | `value: string` | 布尔字面量 |
| `parenthesized` | ParenthesizedExpression | `inner: Option<Expression>` | 括号表达式 |
| `binary` | BinaryExpression | `operators: List<BinaryOperator>`, `operands: List<Expression>` | 二元运算（支持链式） |
| `unary` | UnaryExpression | `operator_value: UnaryOperator`, `operand: Option<Expression>` | 一元运算 |
| `member_access` | MemberAccessExpression | `base_expr: Option<Expression>`, `member_name: string` | 成员访问 `a.b` |
| `function_call` | FunctionCallExpression | `callee: Option<Expression>`, `arguments: List<Expression>` | 函数调用 |
| `if_expr` | IfExpression | `condition`, `then_block`, `else_block` (all `Option<Expression>`) | if 表达式 |
| `while_expr` | WhileExpression | `condition`, `body` (all `Option<Expression>`) | while 表达式 |
| `code_block` | CodeBlockExpression | `statements: List<Statement>`, `trailing_expr: Option<Expression>` | 代码块 |
| `cast_expr` | CastExpression | `type_name: string`, `inner: Option<Expression>` | 类型转换 |
| `new_expr` | NewExpression | `type_name: string`, `arguments: List<Expression>` | new 表达式 |
| `lambda_expr` | LambdaFunctionExpression | `parameters`, `return_type`, `body`, `is_async` | lambda 函数 |
| `spawn_async` | SpawnAsyncExpression | `expression: Option<Expression>` | async 启动 |
| `wait_expr` | WaitExpression | `expression: Option<Expression>` | wait 表达式 |
| `void_literal` | VoidLiteralExpression | `value: string` | void 字面量 |

---

### 语句 (Statement 枚举)

| 变体 | 类名 | 字段 | 说明 |
|------|------|------|------|
| `expression` | ExpressionStatement | `expression: Option<Expression>` | 表达式语句 |
| `block_expr` | BlockExpressionStatement | `expression: Option<Expression>` | 块表达式语句 |
| `assignment` | AssignmentStatement | `target`, `operator_value`, `value` | 赋值语句 |
| `if_stmt` | IfStatement | `condition`, `then_statement`, `else_statement` | if 语句 |
| `while_stmt` | WhileStatement | `condition`, `body` | while 语句 |
| `for_stmt` | ForStatement | `variable_name`, `variable_type`, `is_mutable`, `iterable`, `body` | for 循环 |
| `return_stmt` | ReturnStatement | `value: Option<Expression>` | return 语句 |
| `break_stmt` | BreakStatement | `value: Option<Expression>` | break 语句 |
| `continue_stmt` | ContinueStatement | — | continue 语句 |
| `let_decl` | LetDeclarationStatement | `is_mutable`, `variable_name`, `variable_type`, `initializer` | let 声明 |
| `emit_event` | EmitEventStatement | `event_expr`, `argument` | emit 事件 |
| `yield_stmt` | YieldStatement | `value: Option<Expression>` | yield 语句 |
| `signal_stmt` | SignalStatement | `expression: Option<Expression>` | signal 语句 |

---

### 定义 (Definition 枚举)

| 变体 | 类名 | 关键字段 | 说明 |
|------|------|---------|------|
| `function_def` | FunctionDefinition | `name`, `parameters`, `return_type`, `body`, `is_extern`/`is_pure`/`is_async`/`is_new` | 函数定义 |
| `class_def` | ClassDefinition | `name`, `members: List<Definition>` | 类定义 |
| `enum_def` | EnumDefinition | `name`, `members: List<Definition>` | 枚举定义 |
| `namespace_def` | NamespaceDefinition | `name`, `children: List<Definition>` | 命名空间 |
| `initial_routine` | InitialRoutineDefinition | `body: Option<Expression>` | initial 块 |
| `interface_def` | InterfaceDefinition | `name`, `members` | 接口定义 |
| `impl_def` | InterfaceImplementation | `type_name`, `functions` | impl 块（类内） |
| `impl_for_def` | InterfaceForImplementation | `type_name`, `for_type_name`, `functions` | impl...for 块 |
| `event_def` | EventDefinition | `name`, `event_type` | 事件定义 |
| `on_routine_def` | OnRoutineDefinition | `event_name`, `parameter`, `body` | on 事件处理 |
| `type_ref_def` | TypeReferenceDefinition | `name`, `type_spec` | 类型别名 |
| `enum_member` | EnumMemberDefinition | `name`, `type_spec` | 枚举成员 |
| `class_field` | ClassFieldDefinition | `name`, `mutability`, `type_spec`, `initializer` | 类字段 |

---

### 辅助类型

**TypeSpecifier** — 类型描述符：
- `name: string` — 类型名
- `generic_args: List<TypeSpecifier>` — 泛型参数
- `is_mutable` / `is_not_mutable` / `is_auto_mutability` — 可变性标记
- `is_function_type` / `is_async_function_type` — 函数类型标记
- `function_params` / `return_type` — 函数类型参数
- `qualified_parts: List<string>` — 限定名部分

**Parameter** — 参数定义：
- `name`, `type_spec`, `default_value`, `is_mutable`, `is_this`

**TemplateDeclaration** — 模板声明：
- `parameters: List<TemplateParameter>` (name + type_constraint)

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
