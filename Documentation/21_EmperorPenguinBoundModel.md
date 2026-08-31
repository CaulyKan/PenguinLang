# 21. EmperorPenguin Bound Model

## Overview

Bound Model（绑定模型）是 AST 和 IR 之间的中间层。它将 AST 的纯语法结构转换为携带语义信息的表示，包括类型解析、符号绑定、作用域管理和接口实现验证。

**源文件位置**: `EmperorPenguin/src/bound/`

**核心文件**:
- `BoundType.penguin` — 类型系统（BoundType、BoundTypeArg、名字修饰）
- `BoundTypeRegistry.penguin` — 类型注册与查找
- `BoundSymbol.penguin` — 符号定义
- `BoundScope.penguin` — 作用域层次
- `BoundExpression.penguin` — 绑定表达式
- `BoundStatement.penguin` — 绑定语句
- `BoundDefinition.penguin` — 绑定定义
- `BoundCompilationUnit.penguin` — 编译单元与 SemanticError
- `SemanticModel.penguin` — 语义分析核心（模型字段、`bind()` 编排、`catch_up_def` 回放）
- `SemanticShared.penguin` — 跨 pass 共享的自由函数与数据类
- `SemanticMetaRewrite.penguin` — `MetaRewriter` 元编程预处理（`run_prepass`，pass 之前运行）
- `SemanticBuildScopes.penguin` — Pass 1 `BuildScopesPass`
- `SemanticResolveTypes.penguin` — Pass 2 `ResolveTypesPass`
- `SemanticMonomorphize.penguin` — Pass 3 `MonomorphizePass`
- `SemanticBindSymbols.penguin` — Pass 4 `BindSymbolsPass`
- `SemanticConstructors.penguin` — Pass 5 `ConstructorsPass`
- `SemanticInterfaces.penguin` — Pass 6 `InterfacesPass`
- `SemanticClassifyValueTypes.penguin` — Pass 7 `ClassifyValueTypesPass`
- `SemanticBindBodies.penguin` — Pass 8a `BindBodiesPass`（语句/函数体绑定）
- `SemanticBindExpressions.penguin` — Pass 8b `BindExpressionsPass`（表达式绑定）
- `SemanticBindMetaCalls.penguin` — Pass 8c `BindMetaCallsPass`（元调用绑定）
- `SemanticValidateControlFlow.penguin` — Pass 9 `ValidateControlFlowPass`
- `EmperorPenguinCompiler.penguin` — 编译器顶层入口（`compile_sources`）
- `BoundTreePrinter.penguin` — Bound Tree 调试打印

---

## 类型系统

### BoundType 类

```
BoundType
├── kind: TypeKind              # 类型分类
├── primitive: PrimitiveType    # 原始类型（当 kind == PrimitiveKind）
├── type_definition: Option<BoundDefinition>  # 类/枚举/接口的定义
├── generic_args: List<BoundTypeArg>  # 泛型参数（type_arg 或 value_arg）
├── mutability: Mutability      # 可变性
└── is_async_function: bool     # 是否异步函数类型
```

`BoundTypeArg` 是统一的模板参数表示（enum）：`type_arg: BoundTypeArgType`（类型参数，内含 `bound_type`）或 `value_arg: BoundTypeArgValue`（值参数：`value_arg_kind: ValueArgSubKind`（Int/Bool/String/Double/Object）、标量值字段、`unique_name`、`object_ref`）。

| 方法 | 说明 |
|------|------|
| `display_name() -> string` | 显示类型名称 |
| `with_mutability(m) -> BoundType` | 返回修改可变性后的副本 |
| `with_generic_args(args) -> BoundType` | 返回替换泛型参数后的副本 |
| `is_same_type(other) -> bool` | 类型相等判断（含模板+args 与特化 def 的等价识别） |
| `is_value_type() -> bool` | 是否值类型（原始类型、枚举、ICopy 值类） |
| `is_reference_type() -> bool` | 是否引用类型（引用类、接口） |

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
| `variable` | BoundVariableSymbol | `name`, `full_name`, `bound_type`, `variable_kind`, `is_mutable`, `parameter_index`, `declaring_scope_id`, `enclosing_scope`, `location` |
| `function_sym` | BoundFunctionSymbol | `name`, `full_name`, `bound_type`, `parameters`, `return_type`, `resolved_return_type`, `is_extern`, `is_static`, `is_pure`, `is_new`, `is_async`, `is_meta`, `enclosing_scope`, `location` |
| `type_sym` | BoundTypeSymbol | `name`, `full_name`, `bound_type`, `type_definition`, `generic_params`, `enclosing_scope`, `location` |
| `enum_member` | BoundEnumMemberSymbol | `name`, `full_name`, `bound_type`, `enum_value`, `member_type`, `enclosing_scope`, `location` |
| `namespace_sym` | BoundNamespaceSymbol | `name`, `full_name`, `namespace_scope`, `enclosing_scope`, `location` |

**公共方法**（通过 BoundSymbol 枚举分发）：
- `get_name() -> string`
- `get_full_name() -> string`
- `get_enclosing_scope() -> Option<BoundScope>`

### VariableSymbolKind 枚举

`Local` | `Param` | `Field` | `StaticField` | `Temp` | `Global`

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
| `lookup_symbol(name) -> Option<BoundSymbol>` | 查找符号:本地 → 父链（仅符号） → using 导入（自身优先，逐级向上） → `__builtin`（默认 using） |
| `lookup_symbol_local(name) -> Option<BoundSymbol>` | 仅查找当前作用域 |
| `lookup_type_in_scope(name) -> Option<BoundSymbol>` | 类型查找（同上顺序，`type_sym` 变体） |
| `lookup_namespace(name) -> Option<BoundScope>` | 命名空间查找 |
| `resolve_qualified(parts) -> Option<BoundSymbol>` | 限定名解析 |
| `lookup_imported_symbol(name)` / `lookup_imported_type(name)` | using 导入查找（`__builtin` 恒附加；导入不传递） |
| `lookup_symbol_anywhere(name)` / `lookup_type_anywhere(name)` | 旧的全局子命名空间扫描，仅供语义模型内部使用（元编程占位判定、元模板查找），不得用于用户代码解析 |
| `add_or_merge_namespace(name) -> BoundScope` | 添加/合并命名空间 |
| `add_symbol(symbol)` | 添加符号 |
| `add_child(child)` | 添加子作用域 |

### 命名空间可见性规则（与 BabyPenguin 对齐）

- **`using <ns>;`**（文件顶层或 `namespace` 体内）把命名空间加入该作用域的 `imported_namespaces`；非限定查找在父链之后咨询导入，且**不传递**（只看被导入命名空间的直接符号）。
- **`__builtin` 默认 using**：所有查找链最后恒定尝试 `__builtin` 命名空间。
- **文件级匿名命名空间**：顶层（不在任何 `namespace` 内）的定义绑定到每文件独占的 `_ns_<stem>_<hash16>` 命名空间（C++ static 语义）——同文件内非限定可见，跨文件需限定访问；显式命名空间仍按名跨文件合并。

---

## 绑定表达式 (BoundExpression 枚举)

与 AST 表达式对应，但携带类型信息和符号引用：

| 变体 | 类名 | 额外信息（相比 AST） |
|------|------|---------|
| `literal` | BoundLiteralExpression | `bound_type`, `literal_kind` (IntegerLiteral/FloatLiteral/StringLiteral/BoolLiteral/VoidLiteral) |
| `identifier` | BoundIdentifierExpression | `bound_type`, `symbol: Option<BoundSymbol>` |
| `binary` | BoundBinaryExpression | `bound_type`, 使用 AST BinaryOperator |
| `unary` | BoundUnaryExpression | `bound_type`, 使用 AST UnaryOperator |
| `member_access` | BoundMemberAccessExpression | `bound_type`, `member_symbol: Option<BoundSymbol>` |
| `function_call` | BoundFunctionCallExpression | `bound_type`, `callee_symbol`, `is_virtual`, `generic_args`, `is_generic_function_call`, `direct_dispatch` |
| `if_expr` | BoundIfExpression | `bound_type` |
| `while_expr` | BoundWhileExpression | `bound_type` |
| `code_block` | BoundCodeBlockExpression | `bound_type`, `scope` |
| `cast_expr` | BoundCastExpression | `bound_type`, `target_type`, `is_implicit`, `needs_boxing`/`needs_unboxing` |
| `new_expr` | BoundNewExpression | `bound_type`, `type_symbol`, `constructor_symbol` |
| `enum_variant` | BoundEnumVariantExpression | `bound_type`, `enum_type`, `variant_idx`, `variant_symbol`, `payload` |
| `lambda_expr` | BoundLambdaExpression | `bound_type`, `parameters`, `return_type`, `scope` |
| `meta_call` | BoundMetaCallExpression | `bound_type`, `func_name`, `arguments: List<BoundExpression>`, `trailing_block_ast`, `trailing_definition_ast` |
| `try_bind` | BoundTryBindExpression | `bound_type`, `variable_symbol`, `check`（布尔判定）, `extract`（成功时的载荷提取） |

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
| `meta_if_stmt` | BoundMetaIfStatement | `ast_source: Option<Statement>`（保留原始 AST 供元阶段处理） |
| `meta_while_stmt` | BoundMetaWhileStatement | `ast_source: Option<Statement>` |
| `meta_for_stmt` | BoundMetaForStatement | `ast_source: Option<Statement>` |
| `meta_break_stmt` | BoundMetaBreakStatement | — |
| `meta_continue_stmt` | BoundMetaContinueStatement | — |
| `try_catch` | BoundTryCatchStatement | `try_statements`, `catch_var_symbol`, `catch_statements` |

---

## 绑定定义 (BoundDefinition 枚举)

| 变体 | 类名 | 关键字段 |
|------|------|---------|
| `function_def` | BoundFunctionDefinition | `name`, `full_name`, `symbol`, `parameters`, `return_type`, `body`, `scope`, `is_extern`/`is_pure`/`is_static`/`is_new`, `generic_params`, `value_param_*`, `is_lib_export`/`is_specialized` |
| `class_def` | BoundClassDefinition | `name`, `full_name`, `type_symbol`, `bound_type`, `scope`, `fields`, `methods`, `constructors`, `interface_impls`, `vtables`, `constructor`, `is_value_class`, `ast_source` |
| `enum_def` | BoundEnumDefinition | `name`, `full_name`, `type_symbol`, `bound_type`, `scope`, `members: List<BoundEnumMemberDefinition>`, `methods`, `interface_impls`, `vtables` |
| `interface_def` | BoundInterfaceDefinition | `name`, `full_name`, `type_symbol`, `bound_type`, `scope`, `methods`, `interface_impls`, `vtables` |
| `impl_def` | BoundInterfaceImplementation | `interface_type`, `implementing_type`, `methods`, `vtable`, `source_impl_def` |
| `impl_for_def` | BoundInterfaceForImplementation | `interface_type`, `for_type`, `methods`, `vtable` |
| `namespace_def` | BoundNamespaceDefinition | `name`, `full_name`, `children`, `scope` |
| `initial_routine` | BoundInitialRoutineDefinition | `body`, `scope`, `symbol`, `full_name` |
| `type_ref_def` | BoundTypeReferenceDefinition | `name`, `alias_type`, `type_symbol` |
| `class_field` | BoundClassFieldDefinition | `name`, `bound_type`, `field_symbol`, `initializer`, `mutability`, `is_static` |
| `global_var_def` | BoundGlobalVariableDefinition | `name`, `full_name`, `variable_symbol`, `bound_type`, `initializer`, `is_mutable`, `scope` |
| `meta_function_def` | BoundMetaFunctionDefinition | `name`, `full_name`, `parameters: List<MetaParameter>`, `return_type`, `body`, `native_ptr`, `is_compiled` |
| `meta_if_def` | BoundMetaIfDefinition | `ast_source: Option<Definition>` |
| `meta_for_def` | BoundMetaForDefinition | `ast_source: Option<Definition>` |
| `meta_while_def` | BoundMetaWhileDefinition | `ast_source: Option<Definition>` |
| `meta_call_def` | BoundMetaCallDefinition | `call: Option<BoundExpression>` |

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
├── location: SourceLocation
└── has_suspension: bool          # 单元内是否绑定过 wait/async 挂起点
```

### SemanticError 类

```
SemanticError
├── code: ErrorCode
├── message: string
├── location: SourceLocation
└── severity: ErrorSeverity (Error | Warning | Info)
```

---

## SemanticModel — 多 Pass 编排

`SemanticModel` 是语义分析引擎，将 AST 转换为 Bound Tree。每个 pass 是一个独立的协作类，持有 `model: mut Option<SemanticModel>` 反向引用（Option 包装以打破类字段默认构造的循环依赖），对外提供单一 `run()` 入口；`SemanticModel` 在构造函数中装配全部 pass 实例。

`bind()` 在 9 个 pass 之前先运行 `MetaRewriter.run_prepass(unit)`（元编程预处理：收集 `#fun`/`#class`、`#define`/`#if`/`#while` 拼接、JIT 引擎播种）。Pass 3 产生的新特化定义由核心的 `catch_up_def` 按需回放后续 pass，保证泛型实例走完 Pass 4-8。

### Pass 1: Build Scopes (`BuildScopesPass.run(unit, result)`)

遍历 AST `CompilationUnit`，为每个定义创建对应的 `BoundDefinition` 和 `BoundScope`，注册符号到作用域。

- 处理所有定义类型：函数、类、枚举、接口、命名空间、initial 块、impl、impl...for、类型引用、全局变量
- 收集 `#fun`/`#class` 定义，注册 `#specializing` 块，标记 dyn-lib 导出定义

### Pass 2: Resolve Types (`ResolveTypesPass.run(unit, result)`)

遍历 AST 和 Bound Tree 的 index 对齐平行对，解析 `TypeSpecifier` → `BoundType`。处理泛型、限定名、函数类型、可变性，以及 `#template` 值参数替换。

### Pass 3: Monomorphize (`MonomorphizePass.run(unit, result)`)

泛型特化的迭代不动点（类、枚举、函数，最多 10 轮）：实例收集、名字修饰（`mangle_specialization`）、`#specializing` 条件 impl 注入、特化方法 `this` 参数类型修正。新特化的 def 由 `catch_up_def` 回放 Pass 4-8。

### Pass 4: Bind Symbols (`BindSymbolsPass.run(result)`)

为函数与方法绑定参数符号，补全函数与字段的符号信息。

### Pass 5: Constructors (`ConstructorsPass.run(result)`)

为类生成默认构造函数，处理 `is_new` 显式构造函数与字段初始化。

### Pass 6: Interfaces (`InterfacesPass.run(unit, result)`)

构建接口 vtable（类与枚举），处理 `impl` 与 `impl...for` 块，合并接口继承的 vtable。

### Pass 7: Classify Value Types (`ClassifyValueTypesPass.run(result)`)

按接口实现与字段类型判定 ICopy 值类型 / IRef 引用类型；随后 `validate_interface_usage(result)` 校验接口使用合法性。

### Pass 8: Bind Bodies (`BindBodiesPass.run(unit, result)`)

将 AST 表达式/语句转换为绑定表达式/语句，是类型检查与符号引用解析的核心 pass。`BindBodiesPass`（8a，语句/函数体绑定与 `current_unit` 生命周期）分发给两个协作器：

- `BindExpressionsPass`（8b）：字面量、标识符、二元/一元/逻辑运算、成员访问、函数调用（虚调用/泛型调用）、if/while、代码块、cast/new、try-bind、端口语法脱糖
- `BindMetaCallsPass`（8c）：`#fun` 元调用绑定与 JIT 结果拼接、sizeof/address_of/load/store 内建、unique-name trampoline、模板实例路由

### Pass 9: Validate Control Flow (`ValidateControlFlowPass.run(result)`)

验证控制流合法性：非空函数全路径返回、break/continue 的使用位置、return 值类型检查。

Pass 9 之后 `bind()` 还执行静态 connect 拓扑检查（`validate_port_topology`：输出驱动唯一性、输入重复连接、未连接的模块输入），最后把 `errors` 与 `has_suspension` 写入结果。

---

## 关键 API

### SemanticModel.bind()

入口方法：`bind(unit: ast.CompilationUnit, location: SourceLocation) -> BoundCompilationUnit`

按顺序执行元预处理与全部 9 个 pass，返回完整的绑定编译单元。

### 类型解析

`resolve_type_specifier(type_spec: ast.TypeSpecifier, scope: BoundScope) -> BoundType`

将 AST 类型描述符解析为 BoundType，查找作用域链获取用户定义类型。

### 表达式绑定

所有 `bind_*` 方法接受 AST 表达式和作用域，返回 `Option<BoundExpression>`。绑定后的表达式携带完整的类型信息和符号引用。
