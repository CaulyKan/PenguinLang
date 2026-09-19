# 26. EmperorPenguin Bound Model

## Overview

The Bound Model is the intermediate layer between the AST and the IR. It converts the purely syntactic structure of the AST into a representation that carries semantic information, including type resolution, symbol binding, scope management, and interface implementation validation.

**Source file location**: `EmperorPenguin/src/bound/`

**Core files**:
- `BoundType.penguin` — the type system (BoundType, BoundTypeArg, name mangling)
- `BoundTypeRegistry.penguin` — type registration and lookup
- `BoundSymbol.penguin` — symbol definitions
- `BoundScope.penguin` — the scope hierarchy
- `BoundExpression.penguin` — bound expressions
- `BoundStatement.penguin` — bound statements
- `BoundDefinition.penguin` — bound definitions
- `BoundCompilationUnit.penguin` — the compilation unit and SemanticError
- `SemanticModel.penguin` — the semantic analysis core (model fields, `bind()` orchestration, `catch_up_def` replay)
- `SemanticShared.penguin` — free functions and data classes shared across passes
- `SemanticMetaRewrite.penguin` — the `MetaRewriter` metaprogramming pre-pass (`run_prepass`, runs before the passes)
- `SemanticBuildScopes.penguin` — Pass 1 `BuildScopesPass`
- `SemanticResolveTypes.penguin` — Pass 2 `ResolveTypesPass`
- `SemanticMonomorphize.penguin` — Pass 3 `MonomorphizePass`
- `SemanticBindSymbols.penguin` — Pass 4 `BindSymbolsPass`
- `SemanticConstructors.penguin` — Pass 5 `ConstructorsPass`
- `SemanticInterfaces.penguin` — Pass 6 `InterfacesPass`
- `SemanticClassifyValueTypes.penguin` — Pass 7 `ClassifyValueTypesPass`
- `SemanticBindBodies.penguin` — Pass 8a `BindBodiesPass` (statement/function-body binding)
- `SemanticBindExpressions.penguin` — Pass 8b `BindExpressionsPass` (expression binding)
- `SemanticBindMetaCalls.penguin` — Pass 8c `BindMetaCallsPass` (meta-call binding)
- `SemanticValidateControlFlow.penguin` — Pass 9 `ValidateControlFlowPass`
- `EmperorPenguinCompiler.penguin` — the compiler's top-level entry point (`compile_sources`)
- `BoundTreePrinter.penguin` — Bound Tree debug printing

---

## Type System

### The BoundType Class

```
BoundType
├── kind: TypeKind              # type category
├── primitive: PrimitiveType    # primitive type (when kind == PrimitiveKind)
├── type_definition: Option<BoundDefinition>  # definition of the class/enum/interface
├── generic_args: List<BoundTypeArg>  # generic arguments (type_arg or value_arg)
├── mutability: Mutability      # mutability
└── is_async_function: bool     # whether this is an async function type
```

`BoundTypeArg` is the unified representation of template parameters (an enum): `type_arg: BoundTypeArgType` (a type argument, containing a `bound_type`) or `value_arg: BoundTypeArgValue` (a value argument: `value_arg_kind: ValueArgSubKind` (Int/Bool/String/Double/Object), scalar value fields, `unique_name`, `object_ref`).

| Method | Description |
|------|------|
| `display_name() -> string` | Displays the type name |
| `with_mutability(m) -> BoundType` | Returns a copy with the modified mutability |
| `with_generic_args(args) -> BoundType` | Returns a copy with the generic arguments replaced |
| `is_same_type(other) -> bool` | Type equality test (including equivalence recognition between template+args and specialized defs) |
| `is_value_type() -> bool` | Whether it is a value type (primitives, enums, ICopy value classes) |
| `is_reference_type() -> bool` | Whether it is a reference type (reference classes, interfaces) |

### The TypeKind Enum

`PrimitiveKind` | `ClassKind` | `EnumKind` | `InterfaceKind` | `FunctionKind` | `TypeReferenceKind` | `ErrorKind`

### The PrimitiveType Enum

`I8` | `I16` | `I32` | `I64` | `U8` | `U16` | `U32` | `U64` | `F32` | `F64` | `BoolType` | `CharType` | `StringType` | `VoidType`

### The Mutability Enum

`Mutable` | `Immutable` | `Auto`

### The BoundTypeRegistry Class

Pre-registers all primitive types and provides type lookup and implicit conversion checks.

| Field | Description |
|------|------|
| `bool_type` ~ `void_type` | pre-built primitive types |
| `registered_types: List<NamedTypeEntry>` | registered user types |

| Method | Description |
|------|------|
| `resolve_type(name) -> Option<BoundType>` | Looks up a type by name |
| `register_type(name, t)` | Registers a user type |
| `is_numeric(t) -> bool` | Whether it is a numeric type |
| `is_integer(t) -> bool` | Whether it is an integer type |
| `can_implicitly_cast(from, to) -> bool` | Whether implicit conversion is supported |
| `can_widen_primitive(from, to) -> bool` | Primitive type widening rules |
| `make_function_type(ret, params, is_async) -> BoundType` | Constructs a function type |

---

## Symbol System

### The BoundSymbol Enum

A unified representation of all symbol types:

| Variant | Class | Key fields |
|------|------|---------|
| `variable` | BoundVariableSymbol | `name`, `full_name`, `bound_type`, `variable_kind`, `is_mutable`, `parameter_index`, `declaring_scope_id`, `enclosing_scope`, `location` |
| `function_sym` | BoundFunctionSymbol | `name`, `full_name`, `bound_type`, `parameters`, `return_type`, `resolved_return_type`, `is_extern`, `is_static`, `is_pure`, `is_new`, `is_async`, `is_meta`, `enclosing_scope`, `location` |
| `type_sym` | BoundTypeSymbol | `name`, `full_name`, `bound_type`, `type_definition`, `generic_params`, `enclosing_scope`, `location` |
| `enum_member` | BoundEnumMemberSymbol | `name`, `full_name`, `bound_type`, `enum_value`, `member_type`, `enclosing_scope`, `location` |
| `namespace_sym` | BoundNamespaceSymbol | `name`, `full_name`, `namespace_scope`, `enclosing_scope`, `location` |

**Common methods** (dispatched through the BoundSymbol enum):
- `get_name() -> string`
- `get_full_name() -> string`
- `get_enclosing_scope() -> Option<BoundScope>`

### The VariableSymbolKind Enum

`Local` | `Param` | `Field` | `StaticField` | `Temp` | `Global`

### The BoundFunctionParameter Class

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

## Scope System

### The ScopeKind Enum

`GlobalScope` | `NamespaceScope` | `ClassScope` | `EnumScope` | `InterfaceScope` | `FunctionScope` | `BlockScope` | `InitialRoutineScope` | `ImplScope`

### The BoundScope Class

```
BoundScope
├── kind: ScopeKind
├── name: string
├── full_name: string
├── parent: Option<BoundScope>          # parent scope
├── children: List<BoundScope>          # child scopes
├── symbols: List<BoundSymbol>          # symbols in this scope
└── imported_namespaces: List<string>    # using imports
```

| Method | Description |
|------|------|
| `lookup_symbol(name) -> Option<BoundSymbol>` | Symbol lookup: local → parent chain (symbols only) → using imports (own scope first, walking upward) → `__builtin` (default using) |
| `lookup_symbol_local(name) -> Option<BoundSymbol>` | Looks only in the current scope |
| `lookup_type_in_scope(name) -> Option<BoundSymbol>` | Type lookup (same order as above, the `type_sym` variant) |
| `lookup_namespace(name) -> Option<BoundScope>` | Namespace lookup |
| `resolve_qualified(parts) -> Option<BoundSymbol>` | Qualified-name resolution |
| `lookup_imported_symbol(name)` / `lookup_imported_type(name)` | using-import lookup (`__builtin` is always appended; imports are not transitive) |
| `lookup_symbol_anywhere(name)` / `lookup_type_anywhere(name)` | Legacy global sub-namespace scan, for semantic-model internal use only (metaprogramming placeholder detection, meta template lookup); must not be used for user-code resolution |
| `add_or_merge_namespace(name) -> BoundScope` | Adds/merges a namespace |
| `add_symbol(symbol)` | Adds a symbol |
| `add_child(child)` | Adds a child scope |

### Namespace Visibility Rules (aligned with BabyPenguin)

- **`using <ns>;`** (at file top level or inside a `namespace` body) adds the namespace to that scope's `imported_namespaces`; unqualified lookup consults the imports after the parent chain, and this is **not transitive** (only the direct symbols of the imported namespace are considered).
- **`__builtin` default using**: every lookup chain always tries the `__builtin` namespace last.
- **File-level anonymous namespaces**: top-level definitions (not inside any `namespace`) are bound into a per-file `_ns_<stem>_<hash16>` namespace (C++ static semantics) — unqualified visibility within the same file, qualified access required across files; explicit namespaces are still merged by name across files.

---

## Bound Expressions (the BoundExpression enum)

Correspond to AST expressions, but carry type information and symbol references:

| Variant | Class | Extra information (relative to the AST) |
|------|------|---------|
| `literal` | BoundLiteralExpression | `bound_type`, `literal_kind` (IntegerLiteral/FloatLiteral/StringLiteral/BoolLiteral/VoidLiteral) |
| `identifier` | BoundIdentifierExpression | `bound_type`, `symbol: Option<BoundSymbol>` |
| `binary` | BoundBinaryExpression | `bound_type`, uses the AST BinaryOperator |
| `unary` | BoundUnaryExpression | `bound_type`, uses the AST UnaryOperator |
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
| `try_bind` | BoundTryBindExpression | `bound_type`, `variable_symbol`, `check` (boolean test), `extract` (payload extraction on success) |

All bound expressions provide `get_bound_type() -> BoundType`.

---

## Bound Statements (the BoundStatement enum)

| Variant | Class | Key fields |
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
| `meta_if_stmt` | BoundMetaIfStatement | `ast_source: Option<Statement>` (retains the original AST for meta-phase processing) |
| `meta_while_stmt` | BoundMetaWhileStatement | `ast_source: Option<Statement>` |
| `meta_for_stmt` | BoundMetaForStatement | `ast_source: Option<Statement>` |
| `meta_break_stmt` | BoundMetaBreakStatement | — |
| `meta_continue_stmt` | BoundMetaContinueStatement | — |
| `try_catch` | BoundTryCatchStatement | `try_statements`, `catch_var_symbol`, `catch_statements` |

---

## Bound Definitions (the BoundDefinition enum)

| Variant | Class | Key fields |
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

### VTable Structure

```
BoundVTable
├── interface_type: BoundType
└── slots: List<BoundVTableSlot>
        ├── interface_method: Option<BoundFunctionSymbol>
        └── implementation_method: Option<BoundFunctionSymbol>
```

---

## Compilation Unit

### The BoundCompilationUnit Class

```
BoundCompilationUnit
├── definitions: List<BoundDefinition>
├── global_scope: BoundScope
├── type_registry: BoundTypeRegistry
├── errors: List<SemanticError>
├── location: SourceLocation
└── has_suspension: bool          # whether a wait/async suspension point was bound in this unit
```

### The SemanticError Class

```
SemanticError
├── code: ErrorCode
├── message: string
├── location: SourceLocation
└── severity: ErrorSeverity (Error | Warning | Info)
```

---

## SemanticModel — Multi-Pass Orchestration

`SemanticModel` is the semantic analysis engine that converts the AST into the Bound Tree. Each pass is an independent collaborator class holding a `model: mut Option<SemanticModel>` back-reference (the Option wrapper breaks the circular dependency of class-field default construction) and exposing a single `run()` entry point; `SemanticModel` wires up all pass instances in its constructor.

`bind()` runs `MetaRewriter.run_prepass(unit)` before the 9 passes (the metaprogramming pre-pass: collecting `#fun`/`#class`, `#define`/`#if`/`#while` splicing, JIT engine seeding). Newly specialized definitions produced by Pass 3 are replayed through the subsequent passes on demand by the core `catch_up_def`, ensuring generic instances go through Passes 4-8.

### Pass 1: Build Scopes (`BuildScopesPass.run(unit, result)`)

Walks the AST `CompilationUnit`, creating a corresponding `BoundDefinition` and `BoundScope` for each definition and registering symbols into scopes.

- Handles all definition types: functions, classes, enums, interfaces, namespaces, initial blocks, impl, impl...for, type references, global variables
- Collects `#fun`/`#class` definitions, registers `#specializing` blocks, and marks dyn-lib exported definitions

### Pass 2: Resolve Types (`ResolveTypesPass.run(unit, result)`)

Walks index-aligned parallel pairs of the AST and Bound Tree, resolving `TypeSpecifier` → `BoundType`. Handles generics, qualified names, function types, mutability, and `#template` value-parameter substitution.

### Pass 3: Monomorphize (`MonomorphizePass.run(unit, result)`)

An iterative fixpoint for generic specialization (classes, enums, functions, up to 10 rounds): instantiation collection, name mangling (`mangle_specialization`), `#specializing` conditional-impl injection, and fixing up the `this` parameter types of specialized methods. Newly specialized defs are replayed through Passes 4-8 by `catch_up_def`.

### Pass 4: Bind Symbols (`BindSymbolsPass.run(result)`)

Binds parameter symbols for functions and methods, and completes symbol information for functions and fields.

### Pass 5: Constructors (`ConstructorsPass.run(result)`)

Generates default constructors for classes, and processes `is_new` explicit constructors and field initialization.

### Pass 6: Interfaces (`InterfacesPass.run(unit, result)`)

Builds interface vtables (for classes and enums), processes `impl` and `impl...for` blocks, and merges vtables inherited from interfaces.

### Pass 7: Classify Value Types (`ClassifyValueTypesPass.run(result)`)

Classifies ICopy value types / IRef reference types based on interface implementations and field types; `validate_interface_usage(result)` then validates the legality of interface usage.

### Pass 8: Bind Bodies (`BindBodiesPass.run(unit, result)`)

Converts AST expressions/statements into bound expressions/statements; it is the core pass for type checking and symbol reference resolution. `BindBodiesPass` (8a, statement/function-body binding and the `current_unit` lifecycle) dispatches to two collaborators:

- `BindExpressionsPass` (8b): literals, identifiers, binary/unary/logical operations, member access, function calls (virtual/generic calls), if/while, code blocks, cast/new, try-bind, port syntax desugaring
- `BindMetaCallsPass` (8c): `#fun` meta-call binding and JIT result splicing, sizeof/address_of/load/store intrinsics, unique-name trampolines, template instantiation routing

### Pass 9: Validate Control Flow (`ValidateControlFlowPass.run(result)`)

Validates control-flow legality: non-void functions return on all paths, the placement of break/continue, and return value type checking.

After Pass 9, `bind()` also performs the static connect topology check (`validate_port_topology`: output driver uniqueness, duplicate connections on inputs, unconnected module inputs), and finally writes `errors` and `has_suspension` into the result.

---

## Key APIs

### SemanticModel.bind()

Entry method: `bind(unit: ast.CompilationUnit, location: SourceLocation) -> BoundCompilationUnit`

Runs the meta pre-pass and all 9 passes in order, returning the complete bound compilation unit.

### Type Resolution

`resolve_type_specifier(type_spec: ast.TypeSpecifier, scope: BoundScope) -> BoundType`

Resolves an AST type specifier into a BoundType, walking the scope chain to find user-defined types.

### Expression Binding

All `bind_*` methods accept an AST expression and a scope, and return `Option<BoundExpression>`. Bound expressions carry complete type information and symbol references.
