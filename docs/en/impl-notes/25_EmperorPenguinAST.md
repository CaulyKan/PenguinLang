# 25. EmperorPenguin AST

## Overview

The EmperorPenguin AST (Abstract Syntax Tree) is the output of the compiler frontend's parsing stage. After a source file is tokenized by the Lexer and parsed by the Parser, a `CompilationUnit` is produced, which contains a list of top-level `Definition`s.

**Source file location**: `EmperorPenguin/src/ast/`

**Core files**:
- `Token.penguin` — the TokenType enum, Token, and TokenStream
- `Lexer.penguin` — the lexer (source code → Token stream)
- `Parser.penguin` — the parser (Token stream → CompilationUnit)
- `AST.penguin` — all AST node definitions (expressions, statements, definitions)
- `SourceLocation.penguin` — source locations (filename / line / col)

---

## Token (parser namespace)

### The TokenType Enum

All token types in PenguinLang:

| Category | Token types |
|------|-----------|
| Literals | `EOF`, `Identifier`, `Constant`, `Digitsequence`, `Stringliteral` |
| Internal | `InternalBlocked_return`, `InternalSignal`, `InternalYield_finished_return`, `InternalYield_not_finished_return` |
| Operators | `Minus`/`MinusAssign`, `Arrow`, `Bang`/`BangEqual`, `NotAsync`, `NotMut`, `NotPure`, `Star`/`StarAssign`, `Slash`/`SlashAssign`, `Percent`/`PercentAssign`, `Ampersand`/`AmpersandAmpersand`/`AmpersandAssign`, `Caret`/`CaretAssign`, `Plus`/`PlusAssign`, `Less`/`LessEqual`, `Assign`, `ColonAssign`, `EqualEqual`, `Greater`/`GreaterEqual`, `Pipe`/`PipeAssign`/`PipePipe`, `Tilde` |
| Delimiters | `Comma`, `Semicolon`, `Colon`, `Dot`, `Ellipsis`, `LParen`, `RParen`, `ArrayBracket` (`[]`), `LBrace`, `RBrace`, `Hash` |
| Keywords | `Async`, `Async_fun`, `AutoKw`, `Bool`, `Break`, `Cast`, `UnsafeCast`, `Char`, `Class`, `Continue`, `Double`, `Else`, `ElifKw`, `Enum`, `Extern`, `Export`, `False`, `Float`, `For`, `Fun`, `I8`-`I64`, `U8`-`U64`, `If`, `Impl`, `In`, `Initial`, `Interface`, `Is`, `Let`, `Mut`, `Namespace`, `New`, `Pure`, `Return`, `SelfKw`, `StringKw`, `TemplateKw`, `ThisKw`, `True`, `TypeKw`, `Using`, `Void`, `Wait`, `While`, `Yield` |
| Port/concurrency vocabulary | `Connect`, `ConstructKw`, `Tick`, `TryKw`, `CatchKw`, `Input`, `Output` |

### The Token Class

```
Token
├── token_type: TokenType       # token type
├── text: string                # raw text
└── location: SourceLocation    # source location (filename + line + col)
```

### The TokenStream Class

A token stream supporting lookahead and consumption operations:

```
TokenStream
├── tokens: List<Token>       # list of tokens
└── pos: mut i64              # current position
```

| Method | Description |
|------|------|
| `peek() -> Token` | Peeks at the current token |
| `peek_type() -> TokenType` | Peeks at the current token type |
| `advance() -> Token` | Consumes and returns the current token |
| `expect(expected) -> Token` | Consumes and verifies the current token type |
| `match(expected) -> bool` | Consumes if it matches |
| `is_at_end() -> bool` | Whether the end has been reached |

---

## AST Nodes (ast namespace)

All AST nodes provide a `build_text()` method; expression, statement, and definition nodes implement the `IExpression`, `IStatement`, and `IDefinition` interfaces respectively.

### Operator Enums

**BinaryOperator**:
`Add` | `Subtract` | `Multiply` | `Divide` | `Modulo` | `LessThan` | `GreaterThan` | `LessThanOrEqual` | `GreaterThanOrEqual` | `Equal` | `NotEqual` | `LogicalAnd` | `LogicalOr` | `BitwiseAnd` | `BitwiseOr` | `BitwiseXor` | `Is`

**UnaryOperator**: `Negate` | `Not` | `BitwiseNot` | `UnaryPlus` | `Deref` | `Ref`

**AssignmentOperator**: `Assign` | `PlusAssign` | `MinusAssign` | `StarAssign` | `SlashAssign` | `PercentAssign` | `AmpersandAssign` | `CaretAssign` | `PipeAssign`

---

### Expressions (the Expression enum)

| Variant | Class | Fields | Description |
|------|------|------|------|
| `constant` | ConstantExpression | `value: string` | numeric/character constant |
| `identifier` | IdentifierExpression | `name: string`, `generic_args: List<TypeSpecifier>`, `value_generic_args: List<Expression>` | identifier reference |
| `string_literal` | StringLiteralExpression | `value: string` | string literal |
| `bool_literal` | BoolLiteralExpression | `value: string` | boolean literal |
| `parenthesized` | ParenthesizedExpression | `inner: Option<Expression>` | parenthesized expression |
| `binary` | BinaryExpression | `operators: List<BinaryOperator>`, `operands: List<Expression>` | binary operations (chaining supported) |
| `unary` | UnaryExpression | `operator_value: UnaryOperator`, `operand: Option<Expression>` | unary operation |
| `member_access` | MemberAccessExpression | `base_expr: Option<Expression>`, `member_name: string`, `generic_args: List<TypeSpecifier>` | member access `a.b` |
| `function_call` | FunctionCallExpression | `callee: Option<Expression>`, `arguments: FunctionCallArguments` | function call |
| `function_call_arguments` | FunctionCallArguments | `items: List<Expression>` | function-call argument list (a standalone node, used by the meta engine for parsing) |
| `if_expr` | IfExpression | `condition`, `then_block`, `else_block` (all `Option<Expression>`) | if expression |
| `while_expr` | WhileExpression | `condition`, `body` (all `Option<Expression>`) | while expression |
| `code_block` | CodeBlockExpression | `statements: List<Statement>`, `trailing_expr: Option<Expression>` | code block |
| `cast_expr` | CastExpression | `type_name: string`, `target_type: Option<TypeSpecifier>`, `inner: Option<Expression>`, `is_unsafe: bool` | type cast (`is_unsafe` is `unsafe_cast`) |
| `new_expr` | NewExpression | `type_name: string`, `generic_args: List<TypeSpecifier>`, `arguments: List<Expression>` | new expression |
| `lambda_expr` | LambdaFunctionExpression | `parameters`, `return_type`, `body`, `is_async` | lambda function |
| `spawn_async` | SpawnAsyncExpression | `expression: Option<Expression>` | async spawn |
| `wait_expr` | WaitExpression | `expression: Option<Expression>`, `is_tick_unit: bool` | wait expression |
| `void_literal` | VoidLiteralExpression | `value: string` | void literal |
| `meta_call` | MetaCallExpression | `func_name: string`, `arguments: List<Expression>`, `trailing_block_raw: Option<string>`, `trailing_definition: Option<Definition>` | `#fun(...)` meta call |
| `specializing_impl` | SpecializingImplExpression | `impl_def: Option<InterfaceImplementation>`, `slot: i64` | conditional impl statement inside a `#specializing` block |
| `try_bind` | TryBindExpression | `variable_name: string`, `type_spec: Option<TypeSpecifier>`, `rhs: Option<Expression>` | `let a [: T] := b` try-bind |

---

### Statements (the Statement enum)

| Variant | Class | Fields | Description |
|------|------|------|------|
| `expression` | ExpressionStatement | `expression: Option<Expression>` | expression statement |
| `block_expr` | BlockExpressionStatement | `expression: Option<Expression>` | block-expression statement |
| `assignment` | AssignmentStatement | `target`, `operator_value`, `value` | assignment statement |
| `if_stmt` | IfStatement | `condition`, `then_statement`, `else_statement` | if statement |
| `while_stmt` | WhileStatement | `condition`, `body` | while statement |
| `for_stmt` | ForStatement | `variable_name`, `variable_type`/`type_spec`, `is_mutable`, `iterable`, `body` | for loop |
| `return_stmt` | ReturnStatement | `value: Option<Expression>` | return statement |
| `break_stmt` | BreakStatement | `value: Option<Expression>` | break statement |
| `continue_stmt` | ContinueStatement | — | continue statement |
| `let_decl` | LetDeclarationStatement | `is_mutable`, `variable_name`, `variable_type`/`type_spec`, `initializer` | let declaration |
| `connect_stmt` | ConnectStatement | `source`, `sink` | `connect(source, sink);` (legal only inside a construct block) |
| `try_stmt` | TryStatement | `try_block`, `catch_var_name`, `catch_type_spec`, `catch_block` | try/catch statement |
| `yield_stmt` | YieldStatement | `value: Option<Expression>` | yield statement |
| `signal_stmt` | SignalStatement | `expression: Option<Expression>` | signal statement |
| `meta_if_stmt` | MetaIfStatement | `condition`, `then_statement`, `elif_branches`, `else_statement` | `#if` compile-time statement |
| `meta_while_stmt` | MetaWhileStatement | `condition`, `body` | `#while` compile-time statement |
| `meta_for_stmt` | MetaForStatement | `variable_name`, `variable_type`, `is_mutable`, `iterable`, `body` | `#for` compile-time statement |
| `meta_break_stmt` | MetaBreakStatement | `value: Option<Expression>` | `#break` compile-time statement |
| `meta_continue_stmt` | MetaContinueStatement | — | `#continue` compile-time statement |

---

### Definitions (the Definition enum)

| Variant | Class | Key fields | Description |
|------|------|---------|------|
| `function_def` | FunctionDefinition | `name`, `parameters`, `return_type`, `body`, `is_extern`/`is_pure`/`is_async`/`is_new`, `template_decl` | function definition |
| `class_def` | ClassDefinition | `name`, `members: List<Definition>`, `template_decl` | class definition |
| `enum_def` | EnumDefinition | `name`, `members: List<Definition>`, `template_decl` | enum definition |
| `namespace_def` | NamespaceDefinition | `name`, `children: List<Definition>` | namespace |
| `using_def` | UsingDefinition | `name` | `using <ns>;` directive |
| `initial_routine` | InitialRoutineDefinition | `body: Option<Expression>` | initial block |
| `interface_def` | InterfaceDefinition | `name`, `members`, `template_decl` | interface definition |
| `impl_def` | InterfaceImplementation | `type_name`, `type_spec`, `functions` | impl block (inside a class) |
| `impl_for_def` | InterfaceForImplementation | `type_name`, `for_type_name`, `type_spec`, `for_type_spec`, `functions` | impl...for block |
| `port_def` | PortDefinition | `is_input`, `name`, `type_spec`, `default_expr` | in-class `input`/`output` port declaration |
| `construct_def` | ConstructDefinition | `body: Option<Expression>` | `construct { ... }` refinement block (the only legal scope for connect) |
| `type_ref_def` | TypeReferenceDefinition | `name`, `type_spec` | type alias |
| `enum_member` | EnumMemberDefinition | `name`, `type_spec` | enum member |
| `class_field` | ClassFieldDefinition | `name`, `mutability`, `type_spec`, `initializer` | class field |
| `global_var` | GlobalVariableDefinition | `name`, `type_spec`, `initializer`, `is_mutable` | top-level global variable |
| `meta_function_def` | MetaFunctionDefinition | `name`, `parameters: List<MetaParameter>`, `return_type`, `body` | `#fun` definition |
| `meta_class_def` | ClassDefinition | `name`, `members` | `#class` metadata class definition |
| `meta_if_def` | MetaIfDefinition | `condition`, `then_definitions`, `elif_branches`, `else_definitions` | `#if` definition block |
| `meta_for_def` | MetaForDefinition | `variable_name`, `iterable`, `body: List<Definition>` | `#for` definition block |
| `meta_while_def` | MetaWhileDefinition | `condition`, `body: List<Definition>` | `#while` definition block |
| `meta_call_def` | MetaCallDefinition | `call: Option<Expression>` | top-level `#fun(...)` call |
| `specializing_def` | SpecializingDefinition | `target_type_spec`, `body: List<Expression>`, `impl_fragments` | `#specializing` conditional impl block |

---

### Helper Types

**TypeSpecifier** — type descriptor:
- `name: string` — the type name
- `generic_args: List<TypeSpecifier>` — generic arguments
- `is_mutable` / `is_not_mutable` / `is_auto_mutability` — mutability markers
- `is_value_arg` / `value_arg_text` — value (non-type) generic arguments (e.g. the `5` in `A<5>`)
- `is_iterable` — the array/iterable suffix `[]`
- `is_function_type` / `is_async_function_type` — function-type markers
- `function_params` / `return_type` — function-type parameters
- `qualified_parts: List<string>` — qualified-name parts
- `meta_call: Option<MetaCallExpression>` — a `#fun()` call in type position (returning a type)

**Parameter** — parameter definition:
- `name`, `type_spec`, `default_value`, `is_mutable`, `is_this`

**TemplateDeclaration** — template declaration:
- `parameters: List<TemplateParameter>` (name + type_constraint + the `is_value_param` value-parameter marker)

---

### Compilation Unit

**CompilationUnit** — the top-level AST node:
- `definitions: List<Definition>` — all top-level definitions
- `build_text() -> string` — reconstructs the source text

---

## Position in the Pipeline

```
Source Code → Lexer (TokenStream) → Parser (CompilationUnit) → SemanticModel → Bound Tree → IR
```

The AST is a representation of pure syntactic structure and carries no semantic information (types, symbol references, etc.). Semantic analysis converts the AST into the Bound Tree in `SemanticModel`.
