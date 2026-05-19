# 21. EmperorPenguin Bound Model

## Overview

Bound Model（绑定模型）是 AST 和 IR 之间的中间层。它将 AST 的纯语法结构转换为携带语义信息的表示，包括类型解析、符号绑定、作用域管理和接口实现验证。

**源文件位置**: `EmperorPenguin/src/bound/`

**核心文件**:
- `BoundType.penguin` — 类型系统
- `BoundTypeRegistry.penguin` — 类型注册与查找
- `BoundSymbol.penguin` — 符号定义
- `BoundScope.penguin` — 作用域层次
- `BoundExpression.penguin` — 绑定表达式
- `BoundStatement.penguin` — 绑定语句
- `BoundDefinition.penguin` — 绑定定义
- `BoundCompilationUnit.penguin` — 编译单元
- `SemanticModel.penguin` — 语义分析引擎（多 pass 编排）

---

## 类型系统

### BoundType 类

```
BoundType
├── kind: TypeKind              # 类型分类
├── primitive: PrimitiveType    # 原始类型（当 kind == PrimitiveKind）
├── type_definition: Option<BoundDefinition>  # 类/枚举/接口的定义
├── generic_args: List<BoundType>  # 泛型参数
├── mutability: Mutability      # 可变性
└── is_async_function: bool     # 是否异步函数类型
```

| 方法 | 说明 |
|------|------|
| `display_name() -> string` | 显示类型名称 |
| `with_mutability(m) -> BoundType` | 返回修改可变性后的副本 |
| `is_same_type(other) -> bool` | 类型相等判断 |
| `is_value_type() -> bool` | 是否值类型（原始类型） |
| `is_reference_type() -> bool` | 是否引用类型（类、接口） |

### TypeKind 枚举

`PrimitiveKind` | `ClassKind` | `EnumKind` | `InterfaceKind` | `FunctionKind` | `TypeReferenceKind` | `ErrorKind`

### PrimitiveType 枚举

`I8` | `I16` | `I32` | `I64` | `U8` | `U16` | `U32` | `U64` | `F32` | `F64` | `BoolType` | `CharType` | `StringType` | `VoidType`

### Mutability 枚举

`Mutable` | `Immutable` | `Auto`

### BoundTypeRegistry 类

预注册所有原始类型，提供类型查找和隐式转换判断。

| 字段 | 说明 |
|------|------|
| `bool_type` ~ `void_type` | 预构建的原始类型 |
| `registered_types: List<NamedTypeEntry>` | 已注册的用户类型 |

| 方法 | 说明 |
|------|------|
| `resolve_type(name) -> Option<BoundType>` | 按名查找类型 |
| `register_type(name, t)` | 注册用户类型 |
| `is_numeric(t) -> bool` | 是否数值类型 |
| `is_integer(t) -> bool` | 是否整数类型 |
| `can_implicitly_cast(from, to) -> bool` | 是否支持隐式转换 |
| `can_widen_primitive(from, to) -> bool` | 原始类型拓宽规则 |
| `make_function_type(ret, params, is_async) -> BoundType` | 构造函数类型 |

---

## 符号系统

### BoundSymbol 枚举

所有符号类型的统一表示：

| 变体 | 类名 | 关键字段 |
|------|------|---------|
| `variable` | BoundVariableSymbol | `name`, `full_name`, `bound_type`, `variable_kind`, `is_mutable`, `parameter_index`, `source_line`, `source_col` |
| `function_sym` | BoundFunctionSymbol | `name`, `full_name`, `parameters`, `return_type`, `is_extern`, `is_static`, `is_new`, `is_async`, `source_line`, `source_col` |
| `type_sym` | BoundTypeSymbol | `name`, `full_name`, `bound_type`, `type_definition`, `generic_params`, `source_line`, `source_col` |
| `enum_member` | BoundEnumMemberSymbol | `name`, `full_name`, `enum_value`, `member_type`, `source_line`, `source_col` |
| `namespace_sym` | BoundNamespaceSymbol | `name`, `full_name`, `namespace_scope` |

**公共方法**（通过 BoundSymbol 枚举分发）：
- `get_name() -> string`
- `get_full_name() -> string`
- `get_enclosing_scope() -> Option<BoundScope>`

### VariableSymbolKind 枚举

`Local` | `Parameter` | `Field` | `StaticField` | `Temp`

### BoundFunctionParameter 类

```
BoundFunctionParameter
├── name: string
├── bound_type: BoundType
├── index: i64
├── is_mutable: bool
├── is_this: bool
└── default_value: Option<BoundExpression>
```

---

## 作用域系统

### ScopeKind 枚举

`GlobalScope` | `NamespaceScope` | `ClassScope` | `EnumScope` | `InterfaceScope` | `FunctionScope` | `BlockScope` | `InitialRoutineScope` | `ImplScope`

### BoundScope 类

```
BoundScope
├── kind: ScopeKind
├── name: string
├── full_name: string
├── parent: Option<BoundScope>          # 父作用域
├── children: List<BoundScope>          # 子作用域
├── symbols: List<BoundSymbol>          # 当前作用域的符号
└── imported_namespaces: List<string>    # using 导入
```

| 方法 | 说明 |
|------|------|
| `lookup_symbol(name) -> Option<BoundSymbol>` | 查找符号（含父作用域） |
| `lookup_symbol_local(name) -> Option<BoundSymbol>` | 仅查找当前作用域 |
| `lookup_type_in_scope(name) -> Option<BoundSymbol>` | 类型查找 |
| `lookup_namespace(name) -> Option<BoundScope>` | 命名空间查找 |
| `resolve_qualified(parts) -> Option<BoundSymbol>` | 限定名解析 |
| `lookup_with_imports(name) -> Option<BoundSymbol>` | 含 using 导入的查找 |
| `add_or_merge_namespace(name) -> BoundScope` | 添加/合并命名空间 |
| `add_symbol(symbol)` | 添加符号 |
| `add_child(child)` | 添加子作用域 |

---

## 绑定表达式 (BoundExpression 枚举)

与 AST 表达式对应，但携带类型信息和符号引用：

| 变体 | 类名 | 额外信息（相比 AST） |
|------|------|---------|
| `literal` | BoundLiteralExpression | `bound_type`, `literal_kind` (Integer/Float/String/Bool/Void) |
| `identifier` | BoundIdentifierExpression | `bound_type`, `symbol: Option<BoundSymbol>` |
| `binary` | BoundBinaryExpression | `bound_type`, 使用 AST BinaryOperator |
| `unary` | BoundUnaryExpression | `bound_type`, 使用 AST UnaryOperator |
| `member_access` | BoundMemberAccessExpression | `bound_type`, `member_symbol: Option<BoundSymbol>` |
| `function_call` | BoundFunctionCallExpression | `bound_type`, `callee_symbol`, `is_virtual` |
| `if_expr` | BoundIfExpression | `bound_type` |
| `while_expr` | BoundWhileExpression | `bound_type` |
| `code_block` | BoundCodeBlockExpression | `bound_type`, `scope` |
| `cast_expr` | BoundCastExpression | `bound_type`, `target_type`, `is_implicit` |
| `new_expr` | BoundNewExpression | `bound_type`, `type_symbol`, `constructor_symbol` |
| `enum_variant` | BoundEnumVariantExpression | `bound_type`, `enum_type`, `variant_idx`, `variant_symbol`, `payload` |
| `lambda_expr` | BoundLambdaExpression | `bound_type`, `parameters`, `return_type`, `scope` |

所有绑定表达式都提供 `get_bound_type() -> BoundType`。

---

## 绑定语句 (BoundStatement 枚举)

| 变体 | 类名 | 关键字段 |
|------|------|---------|
| `expression` | BoundExpressionStatement | `expression: Option<BoundExpression>` |
| `assignment` | BoundAssignmentStatement | `target`, `target_symbol`, `operator_value`, `value`, `value_type` |
| `if_stmt` | BoundIfStatement | `condition`, `then_statement`, `else_statement` |
| `while_stmt` | BoundWhileStatement | `condition`, `body` |
| `for_stmt` | BoundForStatement | `loop_variable`, `iterable`, `body`, `scope` |
| `return_stmt` | BoundReturnStatement | `value`, `return_type` |
| `break_stmt` | BoundBreakStatement | `value` |
| `continue_stmt` | BoundContinueStatement | — |
| `let_decl` | BoundLetDeclarationStatement | `variable_symbol`, `initializer`, `bound_type`, `scope` |
| `block` | BoundBlockStatement | `statements`, `scope` |

---

## 绑定定义 (BoundDefinition 枚举)

| 变体 | 类名 | 关键字段 |
|------|------|---------|
| `function_def` | BoundFunctionDefinition | `name`, `full_name`, `symbol`, `parameters`, `return_type`, `body`, `scope`, `is_extern`/`is_pure`/`is_static`/`is_new`, `generic_params` |
| `class_def` | BoundClassDefinition | `name`, `full_name`, `type_symbol`, `bound_type`, `scope`, `fields`, `methods`, `constructors`, `interface_impls`, `vtables`, `constructor` |
| `enum_def` | BoundEnumDefinition | `name`, `full_name`, `type_symbol`, `bound_type`, `scope`, `members: List<BoundEnumMemberDefinition>` |
| `interface_def` | BoundInterfaceDefinition | `name`, `full_name`, `type_symbol`, `bound_type`, `scope`, `methods` |
| `impl_def` | BoundInterfaceImplementation | `interface_type`, `implementing_type`, `methods`, `vtable` |
| `impl_for_def` | BoundInterfaceForImplementation | `interface_type`, `for_type`, `methods`, `vtable` |
| `namespace_def` | BoundNamespaceDefinition | `name`, `full_name`, `children`, `scope` |
| `initial_routine` | BoundInitialRoutineDefinition | `body`, `scope`, `symbol`, `full_name` |
| `type_ref_def` | BoundTypeReferenceDefinition | `name`, `alias_type`, `type_symbol` |
| `class_field` | BoundClassFieldDefinition | `name`, `bound_type`, `field_symbol`, `initializer`, `mutability`, `is_static` |

### VTable 结构

```
BoundVTable
├── interface_type: BoundType
└── slots: List<BoundVTableSlot>
        ├── interface_method: Option<BoundFunctionSymbol>
        └── implementation_method: Option<BoundFunctionSymbol>
```

---

## 编译单元

### BoundCompilationUnit 类

```
BoundCompilationUnit
├── definitions: List<BoundDefinition>
├── global_scope: BoundScope
├── type_registry: BoundTypeRegistry
├── errors: List<SemanticError>
└── source_file: string
```

### SemanticError 类

```
SemanticError
├── message: string
├── line: i64
├── col: i64
└── severity: ErrorSeverity (Error | Warning)
```

---

## SemanticModel — 多 Pass 编排

`SemanticModel` 是语义分析引擎，将 AST 转换为 Bound Tree。处理管线如下：

### Pass 1: Build Scopes (`pass_build_scopes`)

遍历 AST `CompilationUnit`，为每个定义创建对应的 `BoundDefinition` 和 `BoundScope`，注册符号到作用域。

- `bind_definition()` → `bind_function_def()`, `bind_class_def()`, `bind_enum_def()`, `bind_interface_def()`, `bind_namespace_def()`, `bind_initial_routine()`, `bind_impl_def()`, `bind_impl_for_def()`, `bind_type_ref_def()`

### Pass 2: Bind Symbols (`pass_bind_symbols`)

为函数和类的方法绑定符号——创建参数符号、局部变量符号等。

- `bind_symbols_for_def()` → `bind_function_symbols()`, `bind_class_symbols()`

### Pass 3: Resolve Types (`pass_resolve_types`)

遍历 AST 和 Bound Tree 平行对，解析 `TypeSpecifier` → `BoundType`。

- `resolve_pair()` → `resolve_function_pair()`, `resolve_class_pair()`, `resolve_enum_types()`, `resolve_interface_pair()`, `resolve_namespace_pair()`
- `resolve_type_specifier()` — 核心：将 AST 类型描述符转换为 BoundType

### Pass 4: Constructors (`pass_constructors`)

为类生成构造函数，处理字段初始化。

- `init_constructors_for_def()` → `init_class_constructor()`, `init_interface_constructor()`
- `process_constructors_for_def()`

### Pass 5: Interface Implementation (`pass_interface_implementation`)

构建 vtable，处理 `impl` 和 `impl...for` 块。

- `build_vtables_for_def()` → `build_class_vtables()`, `build_interface_vtables()`
- `build_vtable_slots()` — 为每个接口方法分配 vtable 槽位
- `process_impl_for()`

### Pass 6: Bind Expressions (`pass_bind_expressions`)

将 AST 表达式/语句转换为绑定表达式/语句。这是最核心的 pass，处理类型检查、符号引用解析。

- `bind_expression()` → `bind_constant()`, `bind_identifier()`, `bind_binary()`, `bind_unary()`, `bind_member_access()`, `bind_function_call()`, `bind_if_expr()`, `bind_while_expr()`, `bind_code_block()`, `bind_cast()`, `bind_new_expr()`
- `bind_statement()` — 语句绑定

### Pass 7: Validate Control Flow (`pass_validate_control_flow`)

验证控制流合法性（return、break、continue 的使用位置）。

- `validate_expr_control_flow()`, `validate_stmt_control_flow()`
- `expr_always_returns()`, `stmt_always_returns()`

---

## 关键 API

### SemanticModel.bind()

入口方法：`bind(unit: ast.CompilationUnit, source_file: string) -> BoundCompilationUnit`

按顺序执行所有 pass，返回完整的绑定编译单元。

### 类型解析

`resolve_type_specifier(type_spec: ast.TypeSpecifier, scope: BoundScope) -> BoundType`

将 AST 类型描述符解析为 BoundType，查找作用域链获取用户定义类型。

### 表达式绑定

所有 `bind_*` 方法接受 AST 表达式和作用域，返回 `Option<BoundExpression>`。绑定后的表达式携带完整的类型信息和符号引用。
