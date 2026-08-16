# EmperorPenguin

EmperorPenguin 是 Penguin-lang 的自举编译器——用 Penguin-lang 本身编写，生成 LLVM IR 作为输出。它是 BabyPenguin（C# 实现的参考编译器 + VM）的"成年"对应物。

## 设计目标

| 目标 | 解释 |
|------|------|
| **自举能力** | EmperorPenguin 必须能够被自身的某个早期版本编译，从而闭合自举循环。这强制了一些设计决策（例如不使用 `for..in` 语法糖，因为它依赖于标准库） |
| **可预测的编译** | 源代码顺序决定一切——所有源文件被拼接成一个单一的编译单元。没有延迟加载，没有全局符号猜测 |
| **增量复杂度的价值分类** | V8 类被实现为 i8、IValueType 和装箱。编译器首先实现能工作的最小子集（值类型的 i64 算术），然后逐步向上堆叠 |
| **LLVM 后端** | 所有低阶代码生成最终都归约到 LLVM IR 文本，然后由 `clang` 编译为原生代码 |

## 整体架构

```
Penguin Source (`.penguin`)
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                    Lexer（词法分析器）                         │
│        Lexer.penguin — 基于字符的 DFA，逐个字符匹配           │
│        输出: List<Token>                                    │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                    Parser（解析器）                           │
│        Parser.penguin — 手写递归下降解析器                    │
│        输出: ast.CompilationUnit（AST）                     │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                    SemanticModel（语义分析）                  │
│        SemanticModel.penguin — 9-pass 分析引擎               │
│        输出: BoundCompilationUnit（Bound Tree）              │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                    IRGenerator（IR 生成）                    │
│        IRGenerator.penguin — Bound Tree → IR 指令            │
│        输出: IRModule                                       │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                    LLVMEmitter（LLVM 发射）                  │
│        LLVMEmitter.penguin — IR 指令 → LLVM IR 文本         │
│        输出: string（LLVM IR 源文件）                        │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                    C 运行时 + clang                           │
│        core_builtin.c — GC、字符串连接、类型转换              │
│        clang 将 LLVM IR + .a 链接为原生可执行文件             │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
                    原生可执行文件
```

## 模块详解

### 1. 词法分析器 — `src/ast/Lexer.penguin`

**文件:** [`src/ast/Lexer.penguin`](src/ast/Lexer.penguin)

手工编写的 DFA。逐字符扫描，通过顺序关键字匹配（`if (this.match_keyword("fun"))`）识别 token。

**设计考量:**

- 使用**线性链式比较**取代关键字哈希表。这是刻意为之：在自举场景中，一个简单的关键字表会增加编译器的复杂性，而词法分析器的性能并不关键（编译自己的编译器只需要一次）
- 词法分析器负责跟踪行/列号，用于错误报告 → [`Token.penguin`](src/ast/Token.penguin) 中的 `Token.line` 和 `Token.col`
- 对 `u8"..."` 前缀等特殊情况进行硬编码处理

**关键方法:**

| 方法 | 功能 |
|------|------|
| `tokenize()` | 主循环：跳过空白/注释，分派到关键字/操作符/字面量处理 |
| `match_keyword()` | 向前看并匹配关键字字符串而不前进，用于确定性的最长匹配 |
| `read_string_literal()` | 处理转义序列并以原始形式返回字面量 |
| `read_number()` | 读取十进制/十六进制整数字面量 |

### 2. 解析器 — `src/ast/Parser.penguin`

**文件:** [`src/ast/Parser.penguin`](src/ast/Parser.penguin)

递归下降解析器，每个产生式对应一个方法。解析器直接构建 AST 而不使用单独的词法分析器适配层。

**解析器结构（选自约 1800 行源代码）:**

```
parse_compilationUnit()
  └─ parse_definition() 循环
       ├─ parse_functionDefinition()
       ├─ parse_classDefinition()
       ├─ parse_enumDefinition()
       ├─ parse_interfaceDefinition()
       ├─ parse_namespaceDefinition()
       ├─ parse_initialRoutine()
       ├─ parse_interfaceImplementation()    // impl { ... }
       ├─ parse_interfaceForImplementation() // impl IFoo for Bar { ... }
       ├─ parse_typeReferenceDefinition()   // type MyInt = ...;
       └─ parse_globalVariableDefinition()

parse_expression()  // 优先级攀爬
  └─ parse_assignment() → parse_ternary() → parse_logicalOr() → ...
       parse_primaryExpression()  // 最内层
         ├─ 字面量 / 标识符 / this
         ├─ ( expr )
         ├─ { ... }           // 块表达式
         ├─ if (expr) ...     // if 表达式
         ├─ while (expr) ...  // while 表达式
         └─ cast<T>(expr)
```

**设计考量:**

- **无分离的词法分析阶段。** 解析器直接操作 `TokenStream`。这是自举编译器中的常见模式——较少的间接层意味着更简单的代码
- **运算符优先级**通过 Pratt 风格的优先级攀爬实现（`parse_expression` 接受优先级参数，并根据下一个运算符的优先级决定是在当前节点折叠还是递归）
- **表达式和语句之间的"模糊"边界**是刻意设计的。Penguin-lang 中的大多数构造都是表达式（if、while、块）。解析器通过检查 `{` 或 `if`/`while` 是否直接出现在语句上下文中，具有按表达式或按语句解析的启发式方法
- 解析器将错误打印到 `stdout`，但**继续解析**——这允许收集多个错误

**定位锚点:** TokenStream 是按值封装的，因此解析器可以向前看多个 token 而不需要回溯。`peek()`/`peek_type()`/`match()`/`expect()` 模式是典型的 LL(1) 设计。

### 3. AST — `src/ast/AST.penguin`

**文件:** [`src/ast/AST.penguin`](src/ast/AST.penguin)

三种 AST 节点类型，每种都用不同的枚举建模：

| 枚举 | 变体数量 | 用途 |
|------|---------|------|
| `Expression` | 17 | 所有表达式：二元、一元、函数调用、new、cast 等 |
| `Statement` | 13 | 赋值、let、if/while（语句形式）、for、emit、yield、signal |
| `Definition` | 13 | 函数、类、枚举、接口、命名空间、global var、impl、类型引用 |

每个枚举变体都包装了一个专用的类（例如 `Expression.constant: ConstantExpression`）。专用类拥有字段，而枚举提供统一的 `build_text()` 分发。

**设计考量:**

- **所有字段都被标记为 `mut`**，以便在语义分析期间进行后续更新。Ast 节点是可变的，但这反映了它们通过解析器构建、随后由语义模型消费的生命周期
- **`build_text()`** 是每个 node 和 enum 上的一个方法，用于反向呈现源文本——对调试和测试很有价值
- **接口** `IExpression`、`IStatement` 和 `IDefinition` 被用作标记，使得枚举变体在保持统一分发方法的同时拥有多态实现

### 4. Token — `src/ast/Token.penguin`

**文件:** [`src/ast/Token.penguin`](src/ast/Token.penguin)

**`TokenType`** 枚举定义了所有可能的词法单元种类——大约 90 个变体。每个 Penguin-lang 关键字和运算符都有自己的 token 类型：

```
TokenType.Identifier, TokenType.Constant, TokenType.Fun,
TokenType.Class, TokenType.Enum, TokenType.Let, ...
TokenType.Plus, TokenType.Minus, TokenType.Star, ...
TokenType.EqualEqual, TokenType.BangEqual, ...
TokenType.If, TokenType.While, TokenType.For, TokenType.Return, ...
TokenType.LParen, TokenType.RParen, TokenType.LBrace, TokenType.RBrace, ...
```

`TokenStream` 是一个薄的 token 列表包装器，提供：
- `peek()` / `peek_type()` — 向前看而不消费
- `advance()` — 消费并前进
- `expect(expected)` — 前进并断言类型
- `match(expected)` — 如果匹配则前进，返回布尔值

### 5. Bound Tree（语义层）

Bound tree 位于解析 AST 和 IR 生成之间。它在语义分析过程中逐步构建，表示带有**已解析类型**、**已解析符号**和**已验证语义**的程序。

#### 5a. BoundType — `src/bound/BoundType.penguin`

**文件:** [`src/bound/BoundType.penguin`](src/bound/BoundType.penguin)

`BoundType` 是所有类型的统一表示。它是一个带有一个 6 路种类分发的扁平结构体：

```
BoundType
  ├─ kind: TypeKind (PrimitiveKind | ClassKind | EnumKind | InterfaceKind | FunctionKind | TypeReferenceKind | ErrorKind)
  ├─ primitive: PrimitiveType  // 仅当 kind == PrimitiveKind
  ├─ type_definition: Option<BoundDefinition>  // 仅当 kind == ClassKind/EnumKind/InterfaceKind
  ├─ generic_args: List<BoundType>
  ├─ mutability: Mutability (Mutable | Immutable | Auto)
  └─ is_async_function: bool
```

辅助类型：
- **`PrimitiveType`** — 14 个变体（`I8`, `I16`, ..., `StringType`, `VoidType`）
- **`Mutability`** — `Mutable`、`Immutable`、`Auto`

**`BoundTypeRegistry`** 管理全局类型实例并处理：
- 原始类型的预构建单例（`void_type`、`i32_type`、`string_type` 等）
- 函数类型的构造（`make_function_type(return_type, params, is_async)`）
- 基于种类的类型创建辅助方法

#### 5b. BoundSymbol — `src/bound/BoundSymbol.penguin`

**文件:** [`src/bound/BoundSymbol.penguin`](src/bound/BoundSymbol.penguin)

5 种符号，统一放在一个枚举中：

| 变体 | 类 | 用途 |
|------|-----|------|
| `variable` | `BoundVariableSymbol` | 局部变量、参数、字段、全局变量；`VariableSymbolKind` 区分角色 |
| `function_sym` | `BoundFunctionSymbol` | 函数和函数签名；携带参数、返回类型、特性标志 |
| `type_sym` | `BoundTypeSymbol` | 命名类型（类、枚举、接口、类型参数） |
| `enum_member` | `BoundEnumMemberSymbol` | 枚举变体值；携带枚举值 + payload 类型 |
| `namespace_sym` | `BoundNamespaceSymbol` | 命名空间作用域引用 |

每个符号都携带 `name`、`full_name` 和 `enclosing_scope`，以实现解析时的作用域回溯。

#### 5c. BoundScope — `src/bound/BoundScope.penguin`

**文件:** [`src/bound/BoundScope.penguin`](src/bound/BoundScope.penguin)

Penguin-lang 中的作用域形成一棵树。每个作用域都知道其 `parent` 和 `children`：

```
ScopeKind: GlobalScope | NamespaceScope | ClassScope | EnumScope |
           InterfaceScope | FunctionScope | BlockScope | InitialRoutineScope | ImplScope
```

**关键方法:**

| 方法 | 功能 |
|------|------|
| `lookup_symbol(name)` | 从当前作用域向上搜索树 |
| `lookup_symbol_local(name)` | 仅搜索当前作用域 |
| `lookup_type_in_scope(name)` | 仅搜索类型符号 |
| `lookup_namespace(name)` | 在子节点中搜索匹配的命名空间作用域 |
| `resolve_qualified(parts)` | 解析 `A.B.C` 路径 |
| `lookup_with_imports(name)` | 搜索自身 + 导入的命名空间 |
| `add_or_merge_namespace(name)` | 支持跨文件的命名空间合并 |

#### 5d. BoundDefinition — `src/bound/BoundDefinition.penguin`

**文件:** [`src/bound/BoundDefinition.penguin`](src/bound/BoundDefinition.penguin)

10 种定义，组成编译后的程序：

| 变体 | 类 | 关键字段 |
|------|-----|----------|
| `function_def` | `BoundFunctionDefinition` | 参数、返回类型、body（表达式）、作用域、extern/pure/new 标志 |
| `class_def` | `BoundClassDefinition` | 字段、方法、构造函数、interface_impls、vtable、值类型标志 |
| `enum_def` | `BoundEnumDefinition` | 成员（值 + payload 类型）、方法、interface_impls、vtables |
| `interface_def` | `BoundInterfaceDefinition` | 方法、泛型参数 |
| `namespace_def` | `BoundNamespaceDefinition` | 子节点 BoundDefinitions 作用域 |
| `initial_routine` | `BoundInitialRoutineDefinition` | body、作用域、符号 |
| `impl_def` | `BoundInterfaceImplementation` | interface 类型 + 实现方法 + vtable |
| `impl_for_def` | `BoundInterfaceForImplementation` | 与 impl_def 相同，但用于 `impl IFoo for Bar` |
| `global_var_def` | `BoundGlobalVariableDefinition` | 名称、类型、可变性、初始化器 |
| `type_ref_def` | `BoundTypeReferenceDefinition` | 别名 → 底层类型 |

`BoundVTable` 和 `BoundVTableSlot` 存储 interface 方法到实现方法的分派信息。

#### 5e. BoundExpression — `src/bound/BoundExpression.penguin`

**文件:** [`src/bound/BoundExpression.penguin`](src/bound/BoundExpression.penguin)

12 个表达式类 + `BoundExpression` 枚举，镜像 AST 的 `Expression`。每个 bound 表达式都包含一个 `get_bound_type()` 方法，在语义分析期间由类型解析填充。

| 变体 | 类 |
|------|-----|
| `literal` | `BoundLiteralExpression` — 值 + 种类（整数/浮点/字符串/bool/void） |
| `identifier` | `BoundIdentifierExpression` — 符号引用 |
| `binary` | `BoundBinaryExpression` — 运算符 + 操作数 + 类型 |
| `unary` | `BoundUnaryExpression` |
| `member_access` | `BoundMemberAccessExpression` — 基表达式 + 成员符号 |
| `function_call` | `BoundFunctionCallExpression` — 被调用者 + 参数 + 虚分派标志 |
| `if_expr` | `BoundIfExpression` |
| `while_expr` | `BoundWhileExpression` |
| `code_block` | `BoundCodeBlockExpression` |
| `cast_expr` | `BoundCastExpression` — 源类型 → 目标类型 |
| `new_expr` | `BoundNewExpression` — 类型 + 构造函数参数 |
| `enum_variant` | `BoundEnumVariantExpression` — 枚举 + 具体变体 |

#### 5f. BoundStatement — `src/bound/BoundStatement.penguin`

**文件:** [`src/bound/BoundStatement.penguin`](src/bound/BoundStatement.penguin)

10 个语句类 + 分发的 `BoundStatement` 枚举：

| 变体 | 类 |
|------|-----|
| `expression` | `BoundExpressionStatement` — 表达式语句 |
| `assignment` | `BoundAssignmentStatement` — target op value |
| `let_decl` | `BoundLetDeclarationStatement` — 绑定类型 + 符号 + 初始化器 |
| `return_stmt` | `BoundReturnStatement` |
| `break_stmt` | `BoundBreakStatement` |
| `continue_stmt` | `BoundContinueStatement` |
| `if_stmt` | `BoundIfStatement` |
| `while_stmt` | `BoundWhileStatement` |
| `block` | `BoundBlockStatement` |
| `yield_stmt` | `BoundYieldStatement` |

### 6. SemanticModel（语义模型引擎）

**文件:** [`src/bound/SemanticModel.penguin`](src/bound/SemanticModel.penguin)（4181 行——编译器中最复杂的文件）

语义模型使用 `bind()` 调用协调一个多 pass 分析：

```penguin
fun bind(unit: ast.CompilationUnit, source_file: string) -> BoundCompilationUnit {
    this.pass_build_scopes(unit, result);               // Pass 1
    this.pass_resolve_types(unit, result);               // Pass 2
    this.pass_monomorphize(unit, result);                // Pass 3
    this.pass_bind_symbols(result);                      // Pass 4
    this.pass_constructors(result);                      // Pass 5
    this.pass_interface_implementation(unit, result);    // Pass 6
    this.pass_classify_value_types(result);              // Pass 7
    this.pass_bind_expressions(unit, result);            // Pass 8
    this.pass_validate_control_flow(result);             // Pass 9
    result.errors = this.errors;
    return result;
}
```

#### Pass 1 — Build Scopes（作用域构建）

遍历 AST 定义，并为每个定义创建相应的 BoundDefinition。这注册了：
- 用于类型查找的类/枚举/接口符号
- 用于函数调用的函数符号
- 用于名称解析的命名空间作用域树

使用 `add_or_merge_namespace()` 处理跨文件命名空间合并。

#### Pass 2 — Resolve Types（类型解析）

使用 `resolve_type_specifier()` 将 `ast.TypeSpecifier` 解析为 `BoundType`：
- 原始类型（`i32` → `PrimitiveType.I32`）
- 通过 `lookup_type_in_scope()` 查找命名类型
- 泛型实例化（`Box<i32>` → `BoundType(ClassKind, generic_args=[i32])`）
- 处理限定名（`myns.Box` → 递归 `resolve_qualified()`）

#### Pass 3 — Monomorphize（单态化/泛型特化）

最复杂的 pass。对于每个泛型类/枚举/函数的特化：
1. 收集表达式中的泛型实例化（`collect_generic_instantiations`）
2. 将模板 AST 深度复制为特化版本，替换类型参数
3. 在副本上重新运行 Pass 1 + 2（作用域 + 类型解析）
4. 将特化的定义追加到编译单元

使用迭代循环（最多 10 次）来处理传递性依赖：一个特化的类可能在其方法中使用另一个泛型。

*示例:* `Box<i32>` 生成一个 `Box__i32__` 类，其中所有 `T` 出现都被替换为 `i32`。

#### Pass 4 — Bind Symbols（符号绑定）

在函数/类/枚举的作用域中注册参数和字段作为变量符号。这确保在表达式绑定期间，`x` 在被使用时可以被解析为其声明。

#### Pass 5 — Constructors（构造函数）

合并没有显式构造函数的类的默认构造函数。默认构造函数将所有字段初始化为其默认值（0、null 等）。

#### Pass 6 — Interface Implementation（接口实现连接）

将接口方法连接到它们的实现。为每个类构建 `BoundVTable`，`BoundVTableSlot` 将接口方法指针映射到实现。这支持通过接口引用进行的虚分派。

#### Pass 7 — Classify Value Types（值类型分类）

确定每个类是否实现了 `IValueType`，或是递归结构（这会使值类型无效）。标记为 `is_value_class` 的类通过 LLVM 发射器获得值语义（复制传递）。

#### Pass 8 — Bind Expressions（表达式绑定）

AST 表达式 → Bound 表达式的转换。这是语义分析的主要体力劳动。对于每个表达式：
- **字面量:** 解析整数字面量的值类型（`42` 根据返回类型期望可以是 `i32` 或 `i64`）
- **标识符:** 通过作用域查找解析符号
- **二元:** 隐式提升后确定结果类型
- **函数调用:** 解析被调用者，检查参数数量，确定返回类型。处理泛型方法调用（`foo<T>()`）和特化查找
- **new:** 解析类型名，查找构造函数，构建 `BoundNewExpression`
- **member_access:** 解析基表达式的类型，在类型作用域中查找成员
- **cast:** 检查源和目标类型的有效性

#### Pass 9 — Validate Control Flow（控制流验证）

函数末尾的非 void 路径上缺少返回语句的检查。break/continue 在循环外使用的检查。

### 7. 内建函数注册

SemanticModel 的构造函数注册了编译器本身需要的函数：

| 函数 | 签名 | 用途 |
|------|------|------|
| `print` | `(string) -> void` | 标准输出打印 |
| `println` | `(string) -> void` | 标准输出打印并换行 |
| `gc_collect` | `() -> void` | 强制 GC 运行 |
| `gc_info` | `() -> i64` | 获取 GC 堆统计信息 |

内建接口 `ICopy` 和 `IRef` 被注册为空的接口类型占位符。

### 8. IR（中间表示）— `src/ir/`

IR 是一个扁平的指令序列，类似于基本块中间语言。它充当 Bound Tree（面向对象、嵌套）和 LLVM IR（基于基本块、SSA）之间的桥梁。

#### 8a. IRModule — `src/ir/IRModule.penguin`

编译器范围的状态：
- `functions: List<IRFunction>` — 所有函数
- `global_variables: List<IRGlobalVariable>` — 全局变量
- `entry_functions: List<IRFunction>` — 需要生成 `main()` 包装器的入口点
- `source_file: string` — 用于调试信息

#### 8b. IRFunction — `src/ir/IRFunction.penguin`

**IR 中的单个函数：**
- `name: string` / `display_name: string` — 用于链接和调试
- `return_type: string` — 扁平类型名（`"i64"`、`"void"`、`"ref<string>"`）
- `parameters: List<IRParameter>` — 名称、类型、索引
- `instructions: List<IRInstruction>` — 线性指令序列
- 临时寄存器分配器：`next_temp` / `next_label` — 在函数内生成唯一的 SSA 样式的名字

#### 8c. IRInstruction — `src/ir/IRInstruction.penguin`

12 条指令变体：

| 变体 | IR 形式 | 用途 |
|------|---------|------|
| `const_inst` | `%r = CONST 42` | 常量值 |
| `arg_inst` | `%r = ARG x 0` | 参数引用 |
| `assign_inst` | `%r = ASSIGN %src` | 寄存器间复制 |
| `cast_inst` | `%r = CAST %op i64→f32` | 类型转换 |
| `binop_inst` | `%r = BINOP add %a, %b` | 二元运算 |
| `unaryop_inst` | `%r = UNARYOP neg %a` | 一元运算 |
| `call_inst` | `%r = CALL @func(args)` | 函数调用 |
| `ret_inst` | `RET void` / `RET %r` | 返回 |
| `br_inst` | `BR %cond %label_t %label_f` | 条件分支 |
| `jmp_inst` | `JMP %label` | 无条件跳转 |
| `label_inst` | `LABEL %label` | 标签定义 |
| `ptrload_inst` | `%r = PTRLOAD %ptr` | 指针间接（全局/字段访问） |

**关键方法:** `is_terminator()`（用于 RET/BR）和 `is_control_flow()`，用于帮助 IRGenerator 确定何时需要插入终止指令。

#### 8d. IRBuilder — `src/ir/IRBuilder.penguin`

创建 IR 指令的辅助 API。发射函数如：
- `emit_const(value, type, loc)` → 追加 `CONST`
- `emit_binop(op, left, right, type, loc)` → 追加 `BINOP`
- `emit_call(callee, args, ret_type, loc)` → 追加 `CALL`
- `emit_ret(value, loc)` / `emit_ret_void(loc)` → 追加 `RET`

管理临时寄存器（SSA 风格的名字 `%t0`、`%t1`）和标签（`%L0`、`%L1`）。

#### 8e. IRValue — `src/ir/IRValue.penguin`

**IR 值类型** — 所有指令操作数的统一类型：

| 变体 | 用途 |
|------|------|
| `constant` | `IRConstant` — 文字常量 |
| `temp_reg` | `IRTempRegister` — 由 `alloc_temp()` 创建 |
| `named_reg` | `IRNamedRegister` — 命名参数/变量 |
| `global_ref` | `IRGlobalRef` — 全局变量引用 |
| `void_value` | 用于不产生值的操作 |

#### 8f. IRSourceLocation — `src/ir/IRSourceLocation.penguin`

调试的位置信息：`file_path`、`line`、`column`。附加到几乎所有 IR 指令以实现 LLVM 调试元数据。

#### 8g. IRGenerator — `src/ir/IRGenerator.penguin`

**文件:** [`src/ir/IRGenerator.penguin`](src/ir/IRGenerator.penguin)（1282 行）

Bound Tree → IR 的转换器。通过递归降低 Bound 表达式生成线性 IR：

```
generate(unit: BoundCompilationUnit) → IRModule
  └─ lower_definition(def)  — 按定义类型分派
       ├─ lower_function_def(def)
       │    └─ lower_expression(body) → IRValue
       │         ├─ lower_literal() / lower_identifier() / lower_binary()
       │         ├─ lower_function_call() / lower_member_access()
       │         ├─ lower_new() / lower_enum_variant()
       │         ├─ lower_if_expr() / lower_while_expr()
       │         ├─ lower_code_block()  ← 包含语句的降低
       │         │    ├─ lower_statement()
       │         │    │    ├─ lower_return() / lower_let() / lower_assignment()
       │         │    │    ├─ lower_break() / lower_continue()
       │         │    │    └─ lower_if_stmt() / lower_while_stmt()
       │         │    └─ ...
       ├─ lower_class_def(def) — 降低方法 + 构造函数 + impl 方法
       ├─ lower_enum_def(def) — 降低方法
       ├─ lower_interface_def(def) — 降低默认方法
       ├─ lower_initial_routine(def) — 降低 body → 入口函数
       ├─ lower_namespace(def) — 递归
       └─ lower_global_var_def(def) — 注册全局变量 IR 节点
```

**关键设计：**
- 使用 `symbol_regs: List<SymbolRegEntry>` 作为符号 → 寄存器值的映射
- 使用 `loop_stack: List<LoopLabels>` 跟踪循环的 header 和 exit 标签，用于 break/continue
- `reset_locals()` 用于函数边界之间
- 有一个 `IRPrinter` 用于转储 IR 文本以供调试

**类型映射（`bound_type_to_ir_type`）:**

| Bound 类型 | IR 类型 |
|-----------|---------|
| `i8`-`i64`, `u8`-`u64` | `"i8"`-`"i64"` |
| `bool` | `"i8"`（布尔值在 LLVM 中作为字节存储） |
| `f32`/`f64` | `"float"`/`"double"` |
| `string` | `"ref<string>"`（指向 GC 堆的指针） |
| `void` | `"void"` |
| `class == value_type` | `"%class.Foo"` |
| `class == ref_type` | `"%class.Foo*"`（GC 指针） |
| `enum` | `"%enum.Foo"` |
| 函数指针 | `"fun<...>"` |

### 9. LLVM 发射器 — `src/llvm/LLVMEmitter.penguin`

**文件:** [`src/llvm/LLVMEmitter.penguin`](src/llvm/LLVMEmitter.penguin)（2492 行）

接收 `IRModule` 并发出 LLVM IR 文本的多 pass 发射器：

```
lower(module, unit) → string
  1. collect_strings(module)            — 收集所有字符串字面量
  2. build_layout_tables(unit)          — 构建类/枚举的 LLVM 布局
  3. emit_functions(module)             — 发出所有函数体
  4. emit_main(module)                  — 发出 main() 包装器
  5. emit_type_definitions()             — 发出 LLVM %type 定义
  6. emit_global_strings()              — 发出 @str_N 全局变量
  7. emit_global_variables(module)      — 发出 @global_var 定义
  8. emit_extern_declarations(module)   — 发出 declare @func 声明
  9. emit_extern_declarations_runtime() — 发出运行时函数声明
```

#### 布局系统

完成重要的映射工作以确定 LLVM 如何表示类型：

- **`ClassLayout`**: 包含字段偏移、LLVM 结构体类型、vtable 布局、值类型 vs 引用类型
- **`EnumLayout`**: 包含变体索引、payload 类型、vtable
- **vtables**: 存储在对象元数据中；在运行时通过 `_emperor_vtable_lookup` 访问以进行动态分派

#### LLVM IR 发射模式

发射器使用类似汇编器的方法操作 `StringBuilder`：

```
emit_instruction(inst):
  CONST:      %r = add nsw | mul nsw | etc.
  BINOP:      %r = add nsw | sub nsw | mul nsw | sdiv | srem | icmp | and | or | xor
  CALL:       %r = call @func(args)
  RET:        ret void | ret i64 %r
  BR:         br i1 %cond, label %true, label %false
  JMP:        br label %target
  PTRLOAD:    %r = load TYPE, ptr %ptr
```

字符串处理使用运行时函数：`_emperor_string_concat`、`_emperor_int_to_string`、`_emperor_bool_to_string`。

#### 运行时声明

发射器追踪编译器使用了哪些运行时特性，并仅发出必要的 `declare` 语句：

```llvm
declare ptr @_emperor_int_to_string(i32)
declare ptr @_emperor_string_concat(ptr, ptr)
declare ptr @_emperor_vtable_lookup(ptr, ptr, i32)
declare i32 @_emperor_isinstance(ptr, ptr)
declare ptr @_emperor_alloc_impl(i32)
```

### 10. C 运行时 — `std/c/`

**文件:** [`std/c/core_builtin.c`](std/c/core_builtin.c)、[`std/c/gc.c`](std/c/gc.c)、[`std/c/penguinlang_interop.c`](std/c/penguinlang_interop.c)

内置函数的 C 实现：

| 函数 | 实现 |
|------|------|
| `print`/`println` | `printf` + `fflush` |
| `_emperor_int_to_string` | `snprintf` → 分配的字符串 |
| `_emperor_string_concat` | `malloc` + 复制 → 字符串 |
| `_emperor_gc_collect` | 标记-清扫 GC（参见 `gc.c`） |
| `_emperor_gc_info` | 堆使用报告 |

**内存模型：** 带有标记-清扫收集器的垃圾回收堆。引用类型由 GC 追踪；值类型位于栈上。GC 要点（`std/c/gc.c`）：

- **自动回收**：分配量越过阈值（初始 256KB，回收收益低时翻倍）即触发标记-清扫。设置环境变量 `EMPEROR_GC_DISABLE=1` 可在运行时禁用自动回收（诊断用，无需重编译）；显式 `gc_collect()` 不受影响。阈值触发的回收发生在分配返回**之前**，此刻新对象尚无任何可扫描引用 — 新对象在本次回收内被临时标记保护（回收返回后立即复位，绝不让标记泄漏到下一周期，否则对象体扫描会被跳过、其引用的对象将被误回收）。
- **标记**：保守标记 — 扫描栈上每个机器字、GC 对象体的每个字、以及已注册扫描区（见下）的每个字，字值等于某个被追踪指针即标记（`setjmp` 先把 callee-saved 寄存器溢出到栈）。被追踪指针登记在开放寻址哈希表中，查找 O(1)；标记采用显式 worklist 迭代（防深递归爆栈）。误判最多让死对象多活一轮，绝不会误放活对象。
- **扫描区（scan regions）**：`Vector/HashMap/Array` 的元素存储是裸 `_malloc` 缓冲区，保守扫描看不见其**内部**的 GC 指针 — 容器通过 `__builtin._gc_scan_add(addr,size)` 把缓冲区注册为额外扫描区，在 `_grow`/`dispose_mem` 中换绑/注销。未注册的后果是缓冲区里的活元素被回收（`Tests/StdlibTest/StdVectorGcElements.md` 为回归哨兵）；多注册的后果只是多保留垃圾（安全）。
- **终析（finalizer）**：实现 `__builtin.IMemoryDispose` 的**引用类型**（IReferenceType），其 `dispose_mem()` 会被挂到类元数据的 destructor 槽位；sweep 回收死对象前自动调用它 — 释放裸缓冲区**并注销其扫描区**，因此终析对正确性是必要的（泄漏的扫描区只是泄漏，缺注销则误回收）。sweep 分两阶段：先摘链全部死对象、再统一跑终析（死对象图内部引用此刻仍有效）、最后释放。`dispose_mem()` 必须幂等 — 拥有者（如 HashMap）可能先于内层对象自己的终析把它 dispose 掉。手动调用 `dispose_mem()` 依旧有效。
- 值类型（ICopy）不参与终析（装箱副本会与栈上原件双重释放）；程序退出时不做终局回收，存活对象由 OS 回收。

### 11. 入口点 — `main.penguin`

**文件:** [`main.penguin`](main.penguin)

编译器的驱动程序：
1. 读取参数（源文件路径）
2. 加载标准库（`core_builtin.penguin` + `utils.penguin`）
3. 将所有源文件拼接成一个字符串
4. 调用词法分析器 → 解析器 → SemanticModel 管线
5. 调用 IRGenerator → LLVMEmitter
6. 通过 `_utils.get_temp_folder()` 分配一个进程唯一的临时目录（使并行编译互不冲突），写入 `<temp>/combined.ll`
7. 在 `std/c` 上运行 `make OUTPUT_DIR=<temp>` 以构建 `<temp>/libcore_builtin.a`
8. 运行 `clang <temp>/combined.ll <temp>/libcore_builtin.a -o <exe>`（exe 默认为 `<temp>/out.exe`，可用 `-o` 覆盖）
9. 报告成功或收集错误

### 12. 测试基础设施

**位置:** [`EmperorPenguin.Tests/`](../EmperorPenguin.Tests/)

**测试策略：** EmperorPenguin 测试是用 **penguinlang 源代码**编写的，通过 BabyPenguin VM 编译和执行。这在循环中测试了完整的编译器，但有意为之：自举编译器通过自我测试进行测试。

**测试类别：**

| 目录 | 内容 |
|------|------|
| `EndToEndBasicTest.cs` | 完整管线：编译 penguinlang 源文件 → 执行生成的 LLVM → 断言 stdout |
| `BoundTreeBuildScopeTest.cs` | 对 SemanticModel 的 Pass 1 输出运行 penguinlang 脚本 |
| `BoundTreeExpressionTest.cs` | 对 Pass 8（表达式绑定）验证绑定表达式结构 |
| `BoundTreeResolveTypeTest.cs` | 测试类型解析（Pass 2） |
| `BoundTreeInterfaceImplTest.cs` | 测试接口 / vtable 连接 |
| `BoundTreeConstructorTest.cs` | 测试构造函数合成 |
| `BoundTreeControlFlowTest.cs` | 测试控制流验证 |
| `BoundTypeRegistryTest.cs` | 注册表的基本类型创建 |
| `BoundScopeTest.cs` | 作用域层次结构操作 |
| `IRBasicTest.cs` | IR 生成 | 
| `LLVMTest.cs` | LLVM 发射器正确性 |
| `EndToEndClassTest.cs` | 特定于类的端到端测试 |
| `EndToEndEnumTest.cs` | 特定于枚举的端到端测试 |
| `EndToEndGenericTest.cs` | 特定于泛型的端到端测试 |
| `EndToEndInterfaceTest.cs` | 特定于接口的端到端测试 |

**运行测试：**

```bash
dotnet test EmperorPenguin.Tests
```

**`BatchCompiler.cs`** 使用反射收集用源代码注释注解的测试方法，批量编译，然后验证输出。

### 13. 标准库

**位置:** [`std/penguin/`](std/penguin/)

| 文件 | 内容 |
|------|------|
| [`core_builtin.penguin`](std/penguin/core_builtin.penguin) | 核心内建函数的声明（`print`、`println`、`extern` 声明） |
| [`utils.penguin`](std/penguin/utils.penguin) | 编译器使用的辅助函数（`List<T>`、`StringBuilder`、文件 I/O、进程执行） |

标准库在所有源文件之前被预置，因此它们总是可用的——但只能通过编译器内部使用。用户代码无法轻易访问它们，因为用户源码是在标准库之后才被拼接的。

## 跨模块数据流

```
Source Text
  │
  ▼
[Lexer]─────────────────────→ List<Token>
  │
  ▼
[Parser]────────────────────→ ast.CompilationUnit = List<ast.Definition>
  │                               定义包含 Expression/Statement 子节点
  ▼
[SemanticModel]  ─ ─ ─ ─ ─ ─ → BoundCompilationUnit = List<BoundDefinition>
  │                               定义包含 BoundExpression/BoundStatement
  │                               子节点，带有已解析的 BoundType 和 BoundSymbol
  │                               引用
  ▼
[IRGenerator]   ─ ─ ─ ─ ─ ─ → IRModule = List<IRFunction> + List<IRGlobalVariable>
  │                              IRFunction = 线性 List<IRInstruction>
  ▼
[LLVMEmitter]   ─ ─ ─ ─ ─ ─ → string = LLVM IR 源文件
  │
  ▼
 clang + core_builtin.a   ──→ 原生可执行文件
```

## 关键设计决策

### 为什么需要 Bound Tree 而不直接发 IR？

Bound Tree 是一个将语义分析与低阶代码生成解耦的验证层。如果没有它：
- 类型解析逻辑将混在 IR 生成中
- 函数重载解析（通过单态化）需要在 IR 层面上进行，而这并不拥有正确所需的符号信息
- 错误消息（类型不匹配、未解析的符号）会难以生成好的位置信息

### 为什么是单态化而不是运行时泛型？

Penguin-lang 选择 C++ 风格的模板单态化而不是 Java 风格的类型擦除，原因如下：
- **性能：** 为每种类型编译专门的代码可以消除装箱/拆箱
- **值类型：** 值类型在泛型类内部工作无需装箱
- **零开销：** 没有 vtable 间接层，除非被显式接口使用

代价是更长的编译时间和更大的二进制文件——对于自举编译器来说是可以接受的。

### 为什么所有文件都被拼接成一个单元？

简化作用域解析和单态化。通过将所有定义放在一个线性流中，符号总是可发现的，无需跨文件解析。

## 已知限制（截至编写时）

- **泛型函数单态化**：处理 `#template(T: type) fun foo<T>()` 形式的泛型函数，但 10 次迭代的限制意味着极深的嵌套泛型链可能无法完全特化
- **事件/并发**：解析器可以解析 `event`、`emit`、`on`、`wait`、`async`、`folk`，但 LLVM 发射器还**没有**为无栈协程生成正确的状态机转换
- **元编程**：解析器可以解析 `#fun`、`const if`、`const for`，但语义模型**跳过**元函数执行（它们在 `pass_monomorphize` 中被识别但未求值）
- **属性/索引器**：尚未在 Bound 层实现

## 项目文件结构

```
EmperorPenguin/
├── main.penguin                         # 编译器入口点
├── EmperorPenguin.penguins              # 项目文件（源文件列表）
├── src/
│   ├── ast/
│   │   ├── AST.penguin                  # AST 节点定义 + build_text()
│   │   ├── Lexer.penguin                # 词法分析器
│   │   ├── Parser.penguin               # 递归下降解析器
│   │   └── Token.penguin                # Token 类型 + TokenStream
│   ├── bound/
│   │   ├── BoundCompilationUnit.penguin # 编译单元 + SemanticError
│   │   ├── BoundDefinition.penguin      # 语义定义（类、函数等）
│   │   ├── BoundExpression.penguin      # 语义表达式
│   │   ├── BoundScope.penguin           # 作用域树 + 符号解析
│   │   ├── BoundStatement.penguin       # 语义语句
│   │   ├── BoundSymbol.penguin          # 符号类型（变量、函数、类型等）
│   │   ├── BoundTreePrinter.penguin     # Bound Tree 的调试输出
│   │   ├── BoundType.penguin            # 类型系统 + 原始类型定义
│   │   ├── BoundTypeRegistry.penguin    # 类型单例 + 类型创建
│   │   ├── EmperorPenguinCompiler.penguin # 简化编译入口（供测试使用）
│   │   └── SemanticModel.penguin        # 9-pass 语义分析引擎
│   ├── ir/
│   │   ├── IRBuilder.penguin            # IR 构建辅助方法
│   │   ├── IRFunction.penguin           # IR 函数 + 寄存器/标签分配
│   │   ├── IRGenerator.penguin          # Bound Tree → IR 的降低
│   │   ├── IRInstruction.penguin        # IR 指令定义
│   │   ├── IRModule.penguin             # IR 编译单元
│   │   ├── IRPrinter.penguin            # IR 文本转储
│   │   ├── IRSourceLocation.penguin     # 源代码位置信息
│   │   └── IRValue.penguin              # IR 值类型系统
│   └── llvm/
│       └── LLVMEmitter.penguin          # IR → LLVM IR 文本发射器
├── std/
│   ├── c/
│   │   ├── Makefile                     # 构建 .a 以供链接
│   │   ├── core_builtin.c               # print/GCC 内置的 C 实现
│   │   ├── gc.c                         # 标记-清扫 GC 实现
│   │   └── penguinlang_interop.c        # 字符串连接/转换辅助函数
│   ├── include/
│   │   ├── emperor_builtin.h            # 内置函数声明
│   │   ├── emperor_gc.h                 # GC 接口
│   │   ├── emperor_interop.h            # 互操作 API
│   │   └── emperor_types.h              # 类型定义
│   └── penguin/
│       ├── core_builtin.penguin         # 编译器内建函数（print 等）
│       └── utils.penguin                # 编译器实用程序（List、StringBuilder 等）
└── Bridge/                              # (预留——编译器自举桥接)
```
