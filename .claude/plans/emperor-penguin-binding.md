# Plan: EmperorPenguin BoundTree (Semantic Node Tree)

## Context

EmperorPenguin 已完成 AST 解析器阶段（Lexer/Parser/AST nodes），下一步是实现 AST → BoundTree 的转换系统。BoundTree 是语义分析后的中间表示，其中：
- 类型从字符串解析为实际类型引用（`TypeSpecifier` → `BoundType`）
- 标识符绑定到符号声明（`IdentifierExpression` → `BoundIdentifierExpression` + `BoundSymbol`）
- 建立完整的 scope 层次结构
- 完成类型检查和类型推断
- 为后续 EmperorIR 生成提供干净的输入

设计参考了 BabyPenguin 的 SemanticModel/SemanticNode 系统和 C# Roslyn 的 BoundTree。

**v1 不实现**：协程(async/await)、事件(emit/on)、TimingModel、MetaProgramming。

## 关键文件

| 文件 | 用途 |
|------|------|
| `EmperorPenguin/src/ast/AST.penguin` | AST 节点定义（输入） |
| `EmperorPenguin/src/ast/Parser.penguin` | 解析器 |
| `BabyPenguin/SemanticInterface/ICodeContainer.cs` | 表达式绑定逻辑参考 |
| `BabyPenguin/Type/IType.cs` | 类型系统参考 |
| `BabyPenguin/SemanticInterface/ISemanticScope.cs` | Scope 层次参考 |
| `BabyPenguin/SemanticPass/01_SemanticScoping.cs` | Pass 结构参考 |

## 新增文件结构

```
EmperorPenguin/src/bound/
  BoundType.penguin           -- Mutability, PrimitiveType, TypeKind, BoundType
  BoundTypeRegistry.penguin   -- 类型注册中心和查找（primitive + user types）
  BoundSymbol.penguin         -- SymbolKind, VariableSymbolKind, BoundSymbol, BoundFunctionParameter
  BoundScope.penguin          -- ScopeKind, BoundScope（层次化符号查找）
  BoundExpression.penguin     -- LiteralKind, BoundExpression enum + 12 个表达式类
  BoundStatement.penguin      -- BoundStatement enum + 10 个语句类
  BoundDefinition.penguin     -- BoundVTable, BoundDefinition enum + 10 个定义类 + BoundEnumMemberDefinition
  BoundCompilationUnit.penguin -- ErrorSeverity, SemanticError, BoundCompilationUnit
  SemanticModel.penguin       -- 多 pass 绑定编排器
```

注意：由于 PenguinLang 不支持 `Dictionary<K,V>`（内置只有 `List<T>`），类型注册使用 `List` + 线性查找。

## 核心设计

### 1. BoundType — 类型系统 (BoundType.penguin)

**设计原则**：Array(`T[]`)、Option、Result、List 等都是标准库的 class/enum，编译器统一用 Class/Enum 类型处理。`T[]` 在绑定阶段映射为标准库的 `Array<T>` class。

```penguin
enum Mutability { Mutable; Immutable; Auto }

enum PrimitiveType {
    I8; I16; I32; I64;
    U8; U16; U32; U64;
    F32; F64;
    Bool; Char; String; Void;
}

enum TypeKind {
    Primitive;      # i32, bool, string 等编译器内置
    Class;          # 所有 class（含标准库 List, Option, Result 等）
    Enum;           # 所有 enum
    Interface;      # 所有 interface
    Function;       # fun<Ret, P1, P2>（编译器特殊语法）
    TypeReference;  # type alias，指向实际类型
    Error;          # 编译器内部错误恢复，不出现于用户代码
}
```

**BoundType 类**：类型的引用（非声明体），用于变量/参数/返回类型的类型描述。

```penguin
class BoundType {
    kind: mut TypeKind = new TypeKind.Primitive();
    primitive: mut PrimitiveType = new PrimitiveType.Void();    # kind=Primitive 时使用
    type_definition: mut Option<BoundDefinition> = ...;         # kind=Class/Enum/Interface 时指向声明
    generic_args: mut List<BoundType> = ...;                     # 泛型实参
                                                                 # Function: [return_type, param1, param2, ...]
                                                                 # Class/Enum/Interface: [arg1, arg2, ...]
    mutability: mut Mutability = new Mutability.Immutable();
    is_async_function: mut bool = false;                         # async_fun<...> 标记

    # === 关键方法 ===
    fun display_name(this) -> string;           # 人类可读的类型名，如 "List<i32>"
    fun with_mutability(this, m: Mutability) -> BoundType;  # 返回带不同可变性的副本
    fun is_same_type(this, other: BoundType) -> bool;       # 类型相等比较（含泛型参数）
    fun is_value_type(this) -> bool;           # 是否值类型（primitive + 无引用字段的 class）
    fun is_reference_type(this) -> bool;        # 是否引用类型（含引用字段的 class, string）
}
```

**BoundTypeRegistry 类**：类型注册中心，预构建 primitive 类型，提供类型查找和隐式转换判断。

```penguin
class BoundTypeRegistry {
    # 预构建的 primitive 类型实例
    bool_type: BoundType;
    i8_type: BoundType;
    i16_type: BoundType;
    i32_type: BoundType;
    i64_type: BoundType;
    # ... u8..u64, f32, f64, char, string, void

    # 用户注册的类型（标准库 + 用户代码定义的类型）
    registered_types: mut List<BoundType> = ...;

    # === 关键方法 ===
    fun new(mut this);                                           # 初始化 primitive 类型

    fun resolve_type(this, name: string) -> Option<BoundType>;   # 按名查找（先 primitive → 再 registered）
    fun resolve_qualified(this, parts: List<string>) -> Option<BoundType>;  # 限定名解析 Foo.Bar.Baz

    fun register_type(mut this, name: string, t: BoundType);     # 注册用户类型到 registry
    fun register_stdlib_types(mut this);                          # 注册标准库类型（Option, Result, List 等）

    # 类型谓词
    fun is_numeric(this, t: BoundType) -> bool;
    fun is_integer(this, t: BoundType) -> bool;
    fun is_bool(this, t: BoundType) -> bool;
    fun is_void(this, t: BoundType) -> bool;
    fun is_string(this, t: BoundType) -> bool;

    # 隐式转换
    fun can_implicitly_cast(this, from: BoundType, to: BoundType) -> bool;
    fun can_widen_primitive(this, from: PrimitiveType, to: PrimitiveType) -> bool;

    # 工厂方法
    fun make_function_type(this, ret: BoundType, params: List<BoundType>, is_async: bool) -> BoundType;
    fun make_error_type(this) -> BoundType;
}
```

### 2. BoundSymbol — 符号系统 (BoundSymbol.penguin)

**设计原则**：每种符号是独立的类，用 enum BoundSymbol 聚合。与 Expression/Statement/Definition 模式一致。

```penguin
enum VariableSymbolKind { Local; Parameter; Field; StaticField; Temp }

# === 变量符号（局部变量、参数、字段、静态字段、临时变量） ===
class BoundVariableSymbol {
    name: mut string = "";
    full_name: mut string = "";                   # enclosing_scope.full_name + "." + name
    bound_type: mut BoundType = new BoundType();
    variable_kind: mut VariableSymbolKind = new VariableSymbolKind.Local();
    is_mutable: mut bool = false;
    parameter_index: mut i64 = -1;                 # >= 0 表示是函数参数
    declaring_scope_id: mut u64 = 0;               # 声明所在 scope id
    enclosing_scope: mut Option<BoundScope> = ...;
    source_line: mut i64 = 0;
    source_col: mut i64 = 0;
}

# === 函数符号 ===
class BoundFunctionSymbol {
    name: mut string = "";
    full_name: mut string = "";
    bound_type: mut BoundType = new BoundType();   # fun<Ret, P1, P2> 类型
    parameters: mut List<BoundFunctionParameter> = ...;
    return_type: mut BoundType = new BoundType();
    is_extern: mut bool = false;
    is_static: mut bool = false;
    is_pure: mut bool = false;
    is_new: mut bool = false;                       # 构造函数
    is_async: mut bool = false;
    enclosing_scope: mut Option<BoundScope> = ...;
    source_line: mut i64 = 0;
    source_col: mut i64 = 0;
}

# === 类型符号（class, enum, interface, type alias） ===
class BoundTypeSymbol {
    name: mut string = "";
    full_name: mut string = "";
    bound_type: mut BoundType = new BoundType();
    type_definition: mut Option<BoundDefinition> = ...;  # 指向类型声明
    generic_params: mut List<string> = ...;                # 泛型参数名
    enclosing_scope: mut Option<BoundScope> = ...;
    source_line: mut i64 = 0;
    source_col: mut i64 = 0;
}

# === 枚举成员符号 ===
class BoundEnumMemberSymbol {
    name: mut string = "";
    full_name: mut string = "";
    bound_type: mut BoundType = new BoundType();   # 枚举类型本身
    enum_value: mut i64 = 0;                         # 成员序号值
    member_type: mut BoundType = new BoundType();    # 变体 payload 类型（如有的话）
    enclosing_scope: mut Option<BoundScope> = ...;
    source_line: mut i64 = 0;
    source_col: mut i64 = 0;
}

# === 命名空间符号 ===
class BoundNamespaceSymbol {
    name: mut string = "";
    full_name: mut string = "";
    namespace_scope: mut Option<BoundScope> = ...;   # 指向 namespace 的 BoundScope
    enclosing_scope: mut Option<BoundScope> = ...;
    source_line: mut i64 = 0;
    source_col: mut i64 = 0;
}

# === 聚合 enum ===
enum BoundSymbol {
    variable: BoundVariableSymbol;
    function: BoundFunctionSymbol;
    type: BoundTypeSymbol;
    enum_member: BoundEnumMemberSymbol;
    namespace: BoundNamespaceSymbol;

    fun get_name(this) -> string;
    fun get_full_name(this) -> string;
    fun get_enclosing_scope(this) -> Option<BoundScope>;
}

class BoundFunctionParameter {
    name: mut string = "";
    bound_type: mut BoundType = new BoundType();
    index: mut i64 = 0;
    is_mutable: mut bool = false;
    is_this: mut bool = false;
    default_value: mut Option<BoundExpression> = ...;
}
```

**各 Symbol 类的字段对比**：

| 共享字段 | Variable | Function | Type | EnumMember | Namespace |
|----------|----------|----------|------|------------|-----------|
| name | ✓ | ✓ | ✓ | ✓ | ✓ |
| full_name | ✓ | ✓ | ✓ | ✓ | ✓ |
| bound_type | ✓ | ✓ | ✓ | ✓ | |
| enclosing_scope | ✓ | ✓ | ✓ | ✓ | ✓ |
| source_line/col | ✓ | ✓ | ✓ | ✓ | ✓ |
| **特有字段** | variable_kind, is_mutable, parameter_index | parameters, return_type, is_extern/static/pure/new/async | type_definition, generic_params | enum_value, member_type | namespace_scope |

### 3. BoundScope — 作用域层次 (BoundScope.penguin)

**BoundScope**：名字查找容器。通过组合方式被 BoundDefinition 持有（`class_def.scope`）。也独立用于全局 scope 和块 scope。

```penguin
enum ScopeKind { Global; Namespace; Class; Enum; Interface; Function; Block; InitialRoutine; Impl }

class BoundScope {
    kind: mut ScopeKind = new ScopeKind.Global();
    name: mut string = "";
    full_name: mut string = "";                # 如 "<global>.Foo.MyClass"
    parent: mut Option<BoundScope> = ...;
    children: mut List<BoundScope> = ...;
    symbols: mut List<BoundSymbol> = ...;
    imported_namespaces: mut List<string> = ...;  # using 导入的 namespace 名

    # === 基本查找 ===
    fun lookup_symbol(this, name: string) -> Option<BoundSymbol>;     # 当前 scope → 逐级 parent 查找
    fun lookup_symbol_local(this, name: string) -> Option<BoundSymbol>;  # 只在当前 scope 查找
    fun lookup_type_in_scope(this, name: string) -> Option<BoundSymbol>;  # 只找 BoundSymbol.type 变体
    fun lookup_function_in_scope(this, name: string, arg_types: List<BoundType>) -> Option<BoundSymbol>;  # 函数重载，只找 BoundSymbol.function

    # === Namespace 查找 ===
    fun lookup_namespace(this, name: string) -> Option<BoundScope>;  # 在 children 中找 namespace scope
    fun resolve_qualified(this, parts: List<string>) -> Option<BoundSymbol>;  # Foo.Bar.Baz 逐级解析

    # === 带导入的查找 ===
    fun lookup_with_imports(this, name: string) -> Option<BoundSymbol>;  # 先 scope chain，再 imported namespace

    # === Namespace 合并 ===
    fun add_or_merge_namespace(mut this, name: string) -> BoundScope;  # 查找或创建同名 namespace scope

    # === 修改 ===
    fun add_symbol(mut this, symbol: BoundSymbol);   # 设置 symbol.enclosing_scope 和 full_name
    fun add_child(mut this, mut child: BoundScope);  # 设置 child.parent 和 full_name

    # === 遍历 ===
    fun traverse(this, action: fun(BoundScope) -> void);  # 深度优先遍历所有子 scope
    fun find_child_by_name(this, name: string) -> Option<BoundScope>;
}
```

#### Namespace 处理细节

**Qualified Name 解析**（`resolve_qualified(["Foo", "Bar", "Baz"])`）：
1. 从当前 scope 开始，在 children 中查找 "Foo" namespace scope
2. 进入 Foo scope，查找 "Bar" namespace scope
3. 进入 Bar scope，查找 "Baz" type symbol
4. 任何一级未找到 → 尝试 parent scope 重新开始

**Using 导入**（`lookup_with_imports`）：
1. 先调 `lookup_symbol(name)` — scope chain 查找
2. 未找到 → 遍历 `imported_namespaces`
3. 每个 namespace 名通过 `resolve_qualified` 解析到 BoundScope
4. 在该 scope 中 `lookup_symbol_local(name)`
5. 多个匹配 → 报歧义错误

**多文件合并**（`add_or_merge_namespace`）：
1. 检查 children 中是否已有同名 ScopeKind.Namespace
2. 有 → 返回已有 scope（后续定义添加到同一 scope）
3. 无 → 创建新 scope 添加为 child

### 4. BoundExpression — 绑定表达式 (BoundExpression.penguin)

每个表达式携带 `bound_type`（已推断类型）。去掉 `ParenthesizedExpression`（解析关注点）。v1 不含 `SpawnAsyncExpression`, `WaitExpression`。

```penguin
enum LiteralKind { Integer; Float; String; Bool; Void }

class BoundLiteralExpression {
    bound_type: mut BoundType = new BoundType();
    value: mut string = "";
    literal_kind: mut LiteralKind = new LiteralKind.Integer();
    fun get_bound_type(this) -> BoundType;
}

class BoundIdentifierExpression {
    bound_type: mut BoundType = new BoundType();
    symbol: mut Option<BoundSymbol> = ...;       # 已解析的符号引用
    fun get_bound_type(this) -> BoundType;
}

class BoundBinaryExpression {
    bound_type: mut BoundType = new BoundType();
    operators: mut List<BinaryOperator> = ...;   # 复用 AST 的 BinaryOperator
    operands: mut List<BoundExpression> = ...;
    fun get_bound_type(this) -> BoundType;
}

class BoundUnaryExpression {
    bound_type: mut BoundType = new BoundType();
    operator_value: mut UnaryOperator = new UnaryOperator.Negate();  # 复用 AST 的
    operand: mut Option<BoundExpression> = ...;
    fun get_bound_type(this) -> BoundType;
}

class BoundMemberAccessExpression {
    bound_type: mut BoundType = new BoundType();
    base_expr: mut Option<BoundExpression> = ...;
    member_name: mut string = "";
    member_symbol: mut Option<BoundSymbol> = ...;   # 已解析的成员符号
    fun get_bound_type(this) -> BoundType;
}

class BoundFunctionCallExpression {
    bound_type: mut BoundType = new BoundType();
    callee: mut Option<BoundExpression> = ...;      # 被调表达式（可能为 member_access）
    callee_symbol: mut Option<BoundSymbol> = ...;   # 已解析的函数符号
    arguments: mut List<BoundExpression> = ...;
    is_virtual: mut bool = false;                     # 接口方法调用走虚分派
    fun get_bound_type(this) -> BoundType;
}

class BoundIfExpression {
    bound_type: mut BoundType = new BoundType();
    condition: mut Option<BoundExpression> = ...;
    then_block: mut Option<BoundExpression> = ...;
    else_block: mut Option<BoundExpression> = ...;
    fun get_bound_type(this) -> BoundType;
}

class BoundWhileExpression {
    bound_type: mut BoundType = new BoundType();
    condition: mut Option<BoundExpression> = ...;
    body: mut Option<BoundExpression> = ...;
    fun get_bound_type(this) -> BoundType;
}

class BoundCodeBlockExpression {
    bound_type: mut BoundType = new BoundType();
    statements: mut List<BoundStatement> = ...;
    trailing_expr: mut Option<BoundExpression> = ...;  # 代码块的值（如有）
    scope: mut Option<BoundScope> = ...;                 # 块级作用域
    fun get_bound_type(this) -> BoundType;
}

class BoundCastExpression {
    bound_type: mut BoundType = new BoundType();     # 转换后的类型
    target_type: mut BoundType = new BoundType();     # 目标类型
    inner: mut Option<BoundExpression> = ...;
    is_implicit: mut bool = false;                     # 编译器自动插入的隐式转换
    fun get_bound_type(this) -> BoundType;
}

class BoundNewExpression {
    bound_type: mut BoundType = new BoundType();
    type_symbol: mut Option<BoundTypeSymbol> = ...;      # 被实例化的类型
    constructor_symbol: mut Option<BoundFunctionSymbol> = ...;  # 构造函数
    arguments: mut List<BoundExpression> = ...;
    fun get_bound_type(this) -> BoundType;
}

class BoundLambdaExpression {
    bound_type: mut BoundType = new BoundType();
    parameters: mut List<BoundFunctionParameter> = ...;
    return_type: mut BoundType = new BoundType();
    body: mut Option<BoundExpression> = ...;
    scope: mut Option<BoundScope> = ...;                # lambda 自身的作用域
    fun get_bound_type(this) -> BoundType;
}

enum BoundExpression {
    literal: BoundLiteralExpression;
    identifier: BoundIdentifierExpression;
    binary: BoundBinaryExpression;
    unary: BoundUnaryExpression;
    member_access: BoundMemberAccessExpression;
    function_call: BoundFunctionCallExpression;
    if_expr: BoundIfExpression;
    while_expr: BoundWhileExpression;
    code_block: BoundCodeBlockExpression;
    cast_expr: BoundCastExpression;
    new_expr: BoundNewExpression;
    lambda_expr: BoundLambdaExpression;

    fun get_bound_type(this) -> BoundType;  # 所有变体统一委托到各自的 get_bound_type
}
```

### 5. BoundStatement — 绑定语句 (BoundStatement.penguin)

v1 不含 `EmitEventStatement`, `YieldStatement`, `SignalStatement`。

```penguin
class BoundExpressionStatement {
    expression: mut Option<BoundExpression> = ...;
}

class BoundAssignmentStatement {
    target: mut Option<BoundExpression> = ...;          # 赋值目标（已解析为 member_access 或 identifier）
    target_symbol: mut Option<BoundSymbol> = ...;       # 目标的符号引用
    operator_value: mut AssignmentOperator = new AssignmentOperator.Assign();  # 复用 AST 的
    value: mut Option<BoundExpression> = ...;           # 赋值来源
    value_type: mut BoundType = new BoundType();        # 赋值类型（用于类型检查）
}

class BoundIfStatement {
    condition: mut Option<BoundExpression> = ...;
    then_statement: mut Option<BoundStatement> = ...;
    else_statement: mut Option<BoundStatement> = ...;
}

class BoundWhileStatement {
    condition: mut Option<BoundExpression> = ...;
    body: mut Option<BoundStatement> = ...;
}

class BoundForStatement {
    loop_variable: mut Option<BoundVariableSymbol> = ...;  # 循环变量符号
    iterable: mut Option<BoundExpression> = ...;         # 被迭代表达式
    body: mut Option<BoundStatement> = ...;
    scope: mut Option<BoundScope> = ...;                  # for 循环自身的块级作用域
}

class BoundReturnStatement {
    value: mut Option<BoundExpression> = ...;
    return_type: mut BoundType = new BoundType();        # 返回值的类型（用于检查匹配函数签名）
}

class BoundBreakStatement {
    value: mut Option<BoundExpression> = ...;
}

class BoundContinueStatement {
}

class BoundLetDeclarationStatement {
    variable_symbol: mut Option<BoundVariableSymbol> = ...;  # 新创建的变量符号（已加入 scope）
    initializer: mut Option<BoundExpression> = ...;
    bound_type: mut BoundType = new BoundType();          # 变量的类型
    scope: mut Option<BoundScope> = ...;                   # 声明所在的块级作用域
}

class BoundBlockStatement {
    statements: mut List<BoundStatement> = ...;
    scope: mut Option<BoundScope> = ...;                   # 块自身的作用域
}

enum BoundStatement {
    expression: BoundExpressionStatement;
    assignment: BoundAssignmentStatement;
    if_stmt: BoundIfStatement;
    while_stmt: BoundWhileStatement;
    for_stmt: BoundForStatement;
    return_stmt: BoundReturnStatement;
    break_stmt: BoundBreakStatement;
    continue_stmt: BoundContinueStatement;
    let_decl: BoundLetDeclarationStatement;
    block: BoundBlockStatement;
}
```

### 6. BoundDefinition — 绑定定义 (BoundDefinition.penguin)

每个定义通过组合持有 `scope: BoundScope`（名字查找容器）和 `type_symbol: BoundSymbol`（类型符号，用于被其他地方引用）。v1 不含 `EventDefinition`, `OnRoutineDefinition`。

```penguin
# === VTable（接口实现映射表） ===
class BoundVTableSlot {
    interface_method: mut Option<BoundFunctionSymbol> = ...;  # 接口中声明的方法
    implementation_method: mut Option<BoundFunctionSymbol> = ...;  # 具体实现方法
}

class BoundVTable {
    interface_type: mut BoundType = new BoundType();
    slots: mut List<BoundVTableSlot> = ...;
}

# === 定义节点 ===

class BoundFunctionDefinition {
    name: mut string = "";
    full_name: mut string = "";
    symbol: mut Option<BoundFunctionSymbol> = ...;      # 函数符号（可被调用方引用）
    parameters: mut List<BoundFunctionParameter> = ...;
    return_type: mut BoundType = new BoundType();
    body: mut Option<BoundExpression> = ...;             # 函数体（绑定后的 BoundExpression）
    scope: mut Option<BoundScope> = ...;                 # 函数体的作用域（含参数符号）
    is_extern: mut bool = false;
    is_pure: mut bool = false;
    is_static: mut bool = false;
    is_new: mut bool = false;                            # 构造函数
    generic_params: mut List<string> = ...;              # 泛型参数名列表
    fun get_name(this) -> string;
}

class BoundClassDefinition {
    name: mut string = "";
    full_name: mut string = "";
    type_symbol: mut Option<BoundTypeSymbol> = ...;      # 类型符号（BoundSymbol.type）
    bound_type: mut BoundType = new BoundType();         # 此类的 BoundType
    scope: mut Option<BoundScope> = ...;                 # 类级作用域（含字段和方法符号）
    fields: mut List<BoundDefinition> = ...;             # BoundDefinition.class_field 列表
    methods: mut List<BoundDefinition> = ...;            # BoundDefinition.function_def 列表
    constructors: mut List<BoundDefinition> = ...;       # BoundDefinition.function_def(is_new=true) 列表
    vtables: mut List<BoundVTable> = ...;                # 接口实现映射
    generic_params: mut List<string> = ...;
    constructor: mut Option<BoundFunctionDefinition> = ...;  # 主构造器（引用上面的 constructors 列表项）
    fun get_name(this) -> string;
}

class BoundEnumDefinition {
    name: mut string = "";
    full_name: mut string = "";
    type_symbol: mut Option<BoundTypeSymbol> = ...;
    bound_type: mut BoundType = new BoundType();
    scope: mut Option<BoundScope> = ...;
    members: mut List<BoundEnumMemberDefinition> = ...;
    generic_params: mut List<string> = ...;
    value_symbol: mut Option<BoundVariableSymbol> = ...;  # 枚举的 _value 字段符号
    fun get_name(this) -> string;
}

class BoundEnumMemberDefinition {
    name: mut string = "";
    member_symbol: mut Option<BoundEnumMemberSymbol> = ...;
    member_type: mut BoundType = new BoundType();         # 成员关联的类型（如枚举变体的 payload 类型）
    value: mut i64 = 0;                                    # 成员的序号值
}

class BoundInterfaceDefinition {
    name: mut string = "";
    full_name: mut string = "";
    type_symbol: mut Option<BoundTypeSymbol> = ...;
    bound_type: mut BoundType = new BoundType();
    scope: mut Option<BoundScope> = ...;
    methods: mut List<BoundDefinition> = ...;              # 接口方法声明（body 通常为 none）
    generic_params: mut List<string> = ...;
    fun get_name(this) -> string;
}

class BoundInterfaceImplementation {
    interface_type: mut BoundType = new BoundType();       # 被实现的接口类型
    implementing_type: mut BoundType = new BoundType();    # 实现方的类型
    methods: mut List<BoundDefinition> = ...;               # 实现的方法列表
    vtable: mut Option<BoundVTable> = ...;                   # 方法映射表
}

class BoundInterfaceForImplementation {
    interface_type: mut BoundType = new BoundType();       # 被实现的接口类型
    for_type: mut BoundType = new BoundType();              # impl ... for Type 中的 Type
    methods: mut List<BoundDefinition> = ...;
    vtable: mut Option<BoundVTable> = ...;
}

class BoundNamespaceDefinition {
    name: mut string = "";
    full_name: mut string = "";
    children: mut List<BoundDefinition> = ...;             # 命名空间内的所有定义
    scope: mut Option<BoundScope> = ...;                    # 命名空间作用域（支持合并）
    fun get_name(this) -> string;
}

class BoundInitialRoutineDefinition {
    body: mut Option<BoundExpression> = ...;               # initial 块体
    scope: mut Option<BoundScope> = ...;
    symbol: mut Option<BoundFunctionSymbol> = ...;          # initial 函数的符号
    full_name: mut string = "";
    fun get_name(this) -> string;
}

class BoundTypeReferenceDefinition {
    name: mut string = "";                                  # type MyInt = i32 中的 "MyInt"
    alias_type: mut BoundType = new BoundType();            # 指向实际类型 i32
    type_symbol: mut Option<BoundTypeSymbol> = ...;
    fun get_name(this) -> string;
}

class BoundClassFieldDefinition {
    name: mut string = "";
    bound_type: mut BoundType = new BoundType();
    field_symbol: mut Option<BoundVariableSymbol> = ...;  # 字段符号（可被 member_access 引用）
    initializer: mut Option<BoundExpression> = ...;         # 字段初始化表达式
    mutability: mut Mutability = new Mutability.Auto();
    is_static: mut bool = false;
    fun get_name(this) -> string;
}

enum BoundDefinition {
    function_def: BoundFunctionDefinition;
    class_def: BoundClassDefinition;
    enum_def: BoundEnumDefinition;
    interface_def: BoundInterfaceDefinition;
    impl_def: BoundInterfaceImplementation;
    impl_for_def: BoundInterfaceForImplementation;
    namespace_def: BoundNamespaceDefinition;
    initial_routine: BoundInitialRoutineDefinition;
    type_ref_def: BoundTypeReferenceDefinition;
    class_field: BoundClassFieldDefinition;

    fun get_name(this) -> string;  # 所有变体委托到各自的 get_name
}
```

### 7. BoundCompilationUnit — 编译结果 (BoundCompilationUnit.penguin)

```penguin
enum ErrorSeverity { Error; Warning }

class SemanticError {
    message: mut string = "";
    line: mut i64 = 0;
    col: mut i64 = 0;
    severity: mut ErrorSeverity = new ErrorSeverity.Error();
}

class BoundCompilationUnit {
    definitions: mut List<BoundDefinition> = ...;      # 顶层定义列表
    global_scope: mut BoundScope = new BoundScope();   # 全局作用域（namespace 合并的根）
    type_registry: mut BoundTypeRegistry = ...;        # 类型注册中心
    errors: mut List<SemanticError> = ...;              # 所有编译错误/警告
    source_file: mut string = "";

    fun add_error(mut this, error: SemanticError);
    fun has_errors(this) -> bool;
}
```

### 8. SemanticModel — 编排器 (SemanticModel.penguin)

```penguin
class SemanticModel {
    type_registry: mut BoundTypeRegistry;
    errors: mut List<SemanticError> = ...;
    global_scope: mut BoundScope;

    fun new(mut this);  # 初始化 type_registry + global_scope

    # === 主入口 ===
    fun bind(mut this, unit: ast.CompilationUnit, source_file: string) -> BoundCompilationUnit;

    # === 7 个 Pass（内部方法） ===
    fun pass_build_scopes(mut this, unit: ast.CompilationUnit, result: BoundCompilationUnit);
    fun pass_resolve_types(mut this, result: BoundCompilationUnit);
    fun pass_bind_symbols(mut this, result: BoundCompilationUnit);
    fun pass_constructors(mut this, result: BoundCompilationUnit);
    fun pass_interface_implementation(mut this, result: BoundCompilationUnit);
    fun pass_bind_expressions(mut this, result: BoundCompilationUnit);
    fun pass_validate_control_flow(mut this, result: BoundCompilationUnit);

    # === 辅助方法 ===
    fun report_error(mut this, message: string, line: i64, col: i64);
    fun bind_expression(mut this, expr: ast.Expression, scope: BoundScope) -> BoundExpression;
    fun bind_statement(mut this, stmt: ast.Statement, scope: BoundScope) -> BoundStatement;
    fun resolve_type_specifier(this, type_spec: ast.TypeSpecifier, scope: BoundScope) -> BoundType;
}
```

**Pass 详细职责**：

| Pass | 名称 | 遍历对象 | 输入 | 输出 |
|------|------|---------|------|------|
| 1 | Build Scopes | AST Definitions | ast.CompilationUnit | BoundDefinition 层次 + BoundScope 树 + 类型/函数 symbol |
| 2 | Resolve Types | BoundDefinitions | TypeSpecifier (字符串) | BoundType (已解析类型引用) |
| 3 | Bind Symbols | BoundDefinitions | AST Parameter/Field | BoundSymbol (参数/字段/枚举成员) |
| 4 | Constructors | BoundClassDefinitions | class fields | 默认构造器 BoundFunctionDefinition |
| 5 | Interface Impl | BoundInterfaceForImplementation | impl 块 | BoundVTable + 方法映射验证 |
| 6 | Bind Expressions | BoundFunction/Initial bodies | AST Expression/Statement | BoundExpression/BoundStatement + 类型推断 |
| 7 | Validate Control Flow | BoundFunction/Initial bodies | bound body | 返回值路径、break/continue 检查 |

## 与 EmperorIR 的对接

| BoundTree | IR 映射 |
|-----------|---------|
| `BoundLiteralExpression` | CONST |
| `BoundIdentifierExpression` | LOAD（stack slot / field offset） |
| `BoundBinaryExpression` | ADD/SUB/MUL/EQ/NE/... |
| `BoundMemberAccessExpression` | GEP + LOAD |
| `BoundFunctionCallExpression` | CALL / CALL_VIRT |
| `BoundCastExpression` | CAST / BITCAST |
| `BoundNewExpression` | GC_ALLOC + constructor CALL |
| `BoundIfStatement` | BR_COND + basic blocks |
| `BoundWhileStatement` | Loop + BR_COND |
| `BoundLetDeclarationStatement` | ALLOCA |
| `BoundAssignmentStatement` | STORE |
| `BoundReturnStatement` | RET |
| `BoundClassDefinition` | LLVM struct type |
| `BoundVTable` | Virtual dispatch table |

## 实现步骤

**原则**：每完成一个步骤的代码，立刻编写对应的测试用例，确认测试通过后再进入下一步。

### Step 1: 基础类型 + 类型注册中心

创建文件：
- `EmperorPenguin/src/bound/BoundType.penguin` — Mutability, PrimitiveType, TypeKind, BoundType 类
- `EmperorPenguin/src/bound/BoundTypeRegistry.penguin` — BoundTypeRegistry 类（primitive 预构建 + 查找）
- `EmperorPenguin/src/bound/BoundCompilationUnit.penguin` — ErrorSeverity, SemanticError, BoundCompilationUnit

更新项目配置：
- `EmperorPenguin.penguins` 添加 `src/bound/*.penguin`

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过（EmperorPenguin 代码通过 BabyPenguin VM 编译运行）

### Step 2: 测试基础类型

在 `EmperorPenguin.Tests/` 添加 `BoundTypeRegistryTest.cs`：
- primitive 类型查找（resolve_type("i32") → BoundType(primitive=I32)）
- 类型谓词（is_numeric, is_integer, is_bool, is_void）
- display_name 测试
- 隐式转换测试（can_implicitly_cast(i32, i64) == true）
- make_function_type, make_error_type 工厂方法

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 3: 符号系统

创建文件：
- `EmperorPenguin/src/bound/BoundSymbol.penguin` — 5 个符号类 + BoundSymbol enum + BoundFunctionParameter

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 4: 作用域系统

创建文件：
- `EmperorPenguin/src/bound/BoundScope.penguin` — ScopeKind, BoundScope 类

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 5: 测试作用域系统

在 `EmperorPenguin.Tests/` 添加 `BoundScopeTest.cs`：
- add_symbol + lookup_symbol（基本查找）
- parent/children 层次查找
- lookup_type_in_scope（只找 Type 符号）
- add_or_merge_namespace（同名 namespace 合并）
- imported_namespaces + lookup_with_imports（using 导入查找）

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 6: 绑定节点（Expression + Statement + Definition）

创建文件：
- `EmperorPenguin/src/bound/BoundExpression.penguin` — 12 个表达式类 + BoundExpression enum
- `EmperorPenguin/src/bound/BoundStatement.penguin` — 10 个语句类 + BoundStatement enum
- `EmperorPenguin/src/bound/BoundDefinition.penguin` — BoundVTable + 10 个定义类 + BoundDefinition enum + BoundEnumMemberDefinition

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 7: SemanticModel — Pass 1 (Build Scopes)

创建文件：
- `EmperorPenguin/src/bound/SemanticModel.penguin` — SemanticModel 类 + pass_build_scopes 方法

实现 Pass 1 逻辑：
- 遍历 AST CompilationUnit 的每个 Definition
- 为每个定义创建对应的 BoundDefinition
- 构建 BoundScope 层次（namespace 合并、class/enum/interface/function scope）
- 注册类型/函数 symbol 到 scope
- 收集 using 导入

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 8: 测试 Pass 1

在 `EmperorPenguin.Tests/` 添加 `BoundTreeBuildScopeTest.cs`：
- 简单 namespace → BoundNamespaceDefinition + BoundScope 层次
- class 定义 → BoundClassDefinition + scope 中的类型 symbol
- 嵌套 namespace → scope parent/children 正确
- 同名 namespace 合并
- function 定义 → BoundFunctionDefinition + scope 中的函数 symbol
- enum 定义 → BoundEnumDefinition + 成员
- initial 块 → BoundInitialRoutineDefinition
- 类型别名 → BoundTypeReferenceDefinition
- 多定义文件 scope 合并

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 9: SemanticModel — Pass 2 (Resolve Types)

在 SemanticModel 中添加 pass_resolve_types 方法：
- 所有 TypeSpecifier → BoundType
- primitive 直接映射
- 限定名通过 resolve_qualified 解析
- 泛型参数递归解析
- is_iterable 映射为标准库 Array class type

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 10: 测试 Pass 2

在 `EmperorPenguin.Tests/` 添加 `BoundTreeResolveTypeTest.cs`：
- primitive 类型解析（"i32" → BoundType(Primitive, I32)）
- class 类型解析（"MyClass" → BoundType(Class, type_definition→BoundClassDefinition)）
- 泛型类型解析（"List<i32>" → BoundType(Class, generic_args=[BoundType(I32)])）
- 函数类型解析（"fun<i32, i32>" → BoundType(Function)）
- 限定名解析（"Foo.Bar.Baz" → 嵌套 namespace 中的类型）
- type alias 解析
- 未定义类型 → 报错 + Error type

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 11: SemanticModel — Pass 3 (Bind Symbols)

在 SemanticModel 中添加 pass_bind_symbols 方法：
- 函数参数 → BoundFunctionParameter + BoundVariableSymbol
- class 字段 → BoundVariableSymbol(Field)
- 枚举成员 → BoundEnumMemberSymbol + value
- 重复符号检查

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 12: 测试 Pass 3

在 `EmperorPenguin.Tests/` 添加 `BoundTreeBindSymbolTest.cs`：
- 函数参数符号（参数名、类型、index）
- class 字段符号（字段名、类型、variable_kind=Field）
- 枚举成员符号（成员名、enum_value）
- 重复符号报错

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 13: SemanticModel — Pass 4 (Constructors)

在 SemanticModel 中添加 pass_constructors 方法：
- 无显式构造器的类生成默认构造器
- 有字段初始化器的构造器

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 14: 测试 Pass 4

- 无构造器的类 → 自动生成构造器
- 有构造器的类 → 不生成
- 带字段初始化器的类 → 构造器包含初始化逻辑

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 15: SemanticModel — Pass 5 (Interface Implementation)

在 SemanticModel 中添加 pass_interface_implementation 方法：
- impl 块方法绑定
- VTable 构建
- 完整性验证（所有接口方法都已实现）

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 16: 测试 Pass 5

- 简单接口实现 → VTable 正确
- impl ... for ... → 正确映射
- 缺少方法实现 → 报错

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 17: SemanticModel — Pass 6 (Bind Expressions)

在 SemanticModel 中添加 pass_bind_expressions 和辅助方法：
- bind_expression — 12 种表达式的绑定
- bind_statement — 10 种语句的绑定
- 类型推断
- 标识符解析（lookup_with_imports）
- 函数重载解析
- 类型检查

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 18: 测试 Pass 6

- 字面量表达式 → 正确的 BoundType
- 变量引用 → symbol 绑定
- 二元运算 → 结果类型（算术/比较/逻辑）
- 函数调用 → callee_symbol 绑定 + 参数类型匹配
- 成员访问 → member_symbol 绑定
- new 表达式 → 构造器解析
- let 声明 → symbol 创建 + scope 注册
- if/while/for → 条件类型 bool 检查
- 类型推断（无显式类型时推断）
- 类型不匹配报错

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 19: SemanticModel — Pass 7 (Validate Control Flow)

在 SemanticModel 中添加 pass_validate_control_flow 方法：
- 非 void 函数的所有路径必须 return
- break/continue 只出现在循环内
- return 类型匹配函数签名

验证：`dotnet run --project BabyPenguin -- EmperorPenguin.penguins` 通过

### Step 20: 测试 Pass 7

- 非 void 函数缺少 return → 报错
- void 函数不需要 return
- 循环内 break/continue → 正确
- 循环外 break/continue → 报错
- return 类型不匹配 → 报错

验证：`dotnet test EmperorPenguin.Tests` 通过

### Step 21: 集成测试 + main.penguin 更新

- 更新 `main.penguin` 集成 SemanticModel（parse → bind → print bound tree）
- 端到端测试：解析简单程序 → 绑定 → 输出 BoundTree
- 确认所有现有测试仍然通过

验证：
```bash
dotnet build
dotnet test EmperorPenguin.Tests   # 所有测试通过
dotnet test BabyPenguin.Tests       # 不受影响
```

## 验证总览

每个步骤完成后运行：
```bash
dotnet run --project BabyPenguin -- EmperorPenguin.penguins   # EmperorPenguin 代码编译运行
dotnet test EmperorPenguin.Tests                              # 当前步骤测试通过 + 不破坏已有测试
```

最终完成后：
```bash
dotnet test BabyPenguin.Tests                                 # 确保不破坏 BabyPenguin
```

测试覆盖矩阵：

| 测试文件 | 覆盖 Pass | 关键测试点 |
|----------|-----------|-----------|
| BoundTypeRegistryTest.cs | 基础类型 | primitive 查找、类型谓词、隐式转换、display_name |
| BoundScopeTest.cs | Scope 基础 | 查找、层次、namespace 合并、using 导入 |
| BoundTreeBuildScopeTest.cs | Pass 1 | 定义→BoundDefinition、scope 层次、symbol 注册 |
| BoundTreeResolveTypeTest.cs | Pass 2 | 类型解析、泛型、限定名、type alias |
| BoundTreeBindSymbolTest.cs | Pass 3 | 参数/字段/枚举成员符号、重复检查 |
| BoundTreeConstructorTest.cs | Pass 4 | 默认构造器生成 |
| BoundTreeInterfaceTest.cs | Pass 5 | VTable 构建、完整性验证 |
| BoundTreeExpressionTest.cs | Pass 6 | 表达式绑定、类型推断、符号解析 |
| BoundTreeControlFlowTest.cs | Pass 7 | 返回路径、break/continue 检查 |
