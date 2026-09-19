# 元编程

PenguinLang 的元编程在编译期运行普通 PenguinLang 代码，并把结果拼接回被编译的程序。`#` 前缀打开元空间：`#fun`、`#if`、`#while`、`#define`、`#typeof`、`#template`、`#specializing`、`#class`、`#compiler()` 与元调用（`#name(args)`）。

## 表面与可用性

| 构造 | 种类 | 可用性 |
|---|---|---|
| `#template(...)` 前缀 | 返回类型的元函数的语法糖 | 所有编译器（各处解析与单态化） |
| `#define("K","V")` / `#defined("K")` / `#option("K")` | 编译期键值存储 | 所有 EmperorPenguin 构建 |
| `#if` / `#elif` / `#else`、`#while` | 编译期控制流（硬编码折叠） | 所有 EmperorPenguin 构建 |
| `#fun`（JIT 元函数）、`-> type`、反射、值模板参数、`#specializing`、`#class`、`#compiler()`、`-> ast` / `unstructured_ast` | 元引擎（LLVM ORC JIT） | 原生编译器：Pass2、Pass3、LSP |

BabyPenguin（C# 编译器）只解析 `#template`，不执行任何元构造。Pass1（BabyPenguin VM 上的 EmperorPenguin）运行硬编码构造但没有 JIT。

## 执行模型

当 `#fun` 调用需要求值时，编译器把 `#fun` 体（加上编译器自身的 bound/AST 类型层）经自己的流水线编译为 LLVM IR，并在内嵌的 **LLVM ORC JIT** 上执行。参数与结果以 `i64` 令牌跨界；`type` 实参是按名驻留、可解析回活 `BoundType` 指针的令牌。反射是**对象复用**：单元 B（编译期单元）编译了编译器真实的 `emperor.BoundType`/`BoundClassFieldDefinition`/`BoundFunctionDefinition` 类，因此 `#fun` 内的 `t.fields()` 是对活对象的直接方法调用。

元调用在哪一阶段求值取决于其语法位置：

| 位置 | 求值于 |
|---|---|
| 全局/命名空间作用域（定义位置） | 预处理重写，Pass 1 之前 |
| 类型位置（`let x: #fun() -> type`） | Pass 2（类型解析） |
| class/enum/interface 体内 | Pass 4（绑定符号） |
| 函数/例程体内 | Pass 8（绑定表达式） |
| `#template` 体内 | Pass 3（单态化，实例化时） |

## 元函数：#fun

```penguin
#fun sq(n: i64) -> i64 { return n * n; }

initial {
    let a: i64 = #sq(5);        // 以字面量 25 拼接
}
```

规则：

* `#fun` 定义只在全局或命名空间作用域。
* 参数有显式种类——`type`、`ast`、`unstructured_ast`，或普通值类型（`i64`、`bool`、`double`、`string`，引用类型映射为 `object`）。
* 返回类型显式；允许的返回种类是标量、`string`、`type` 与 `ast`。不支持返回引用类型进入运行时代码（仅编译期数据流）。
* 体内递归去掉 `#` 前缀（普通调用）。
* 编译按需进行并按函数缓存。
* 实参过少是硬错误；额外尾随 `{ ... }` 块只交付给 `unstructured_ast` 最后参数。

返回 `type` 的元函数可用于任何类型标注位置：`let x: #signed_to_unsigned(i32) = 0;`。`#template` 是它的语法糖：`#template(T: type) class Box` 的行为如同 `#fun Box(T: type) -> type`。模板值参数（`#template(N: i32)`）按实例化做元求值（仅原生编译器）。

## 元调用语法

```
'#' identifier ('(' args ')')? (';' | trailing_block)
```

* 表达式位置：`#name(args)`——结果在调用点拼接（表达式位置的 `type` 结果是错误；请在类型位置使用）。
* 定义位置：`#name(args)` 后跟花括号块或定义——块/定义文本传给元函数（注解形式；见下文 `unstructured_ast`）。
* 未注册为 `#fun` 的未知 `#name` 调用回退为 `BoundMetaCallExpression` 存续到后续遍（元引擎仍可能解析它们，例如延迟的模板重绑定）。

## 编译期选项：#define / #defined / #option

`#define("K", "V")` 写入编译器的选项存储；`#defined("K") -> bool` 测试存在；`#option("K") -> string` 读取。CLI 的 `-DK=V` 写入同一存储。`#fun` 体内它们也可作为内建元函数并经 `compiler().set_option/get_option/has_option` 使用。

## 编译期条件：#if / #while

```penguin
#if (#defined("A")) { fun pick() -> string { return "a"; } }
#elif (#defined("B")) { fun pick() -> string { return "b"; } }
#else { fun pick() -> string { return "none"; } }
```

* 分支体是定义（定义位置）或语句（语句位置）的花括号块。
* 条件由字面量、`#defined`、`#option` 比较、`!`、`&&`、`||`、`==`、`!=` 与括号折叠。非常量条件报 `E_UNSUPPORTED`。
* `#elif`/`#else` 带 `#` 前缀（它们是解析器关键字，区别于运行时 `else`）。
* `#while` 以 10000 次上限展开。`#for` / `#break` / `#continue` 已可解析；集合迭代未实现。

## #typeof(T)

`#typeof(T)` 产出 `T` 的类型令牌。它在 `#fun` 体内可用（`#typeof(T) == #typeof(i32)` 比较是驻留令牌的指针相等）也可在普通代码中用——模板体内解析为具体实例化类型。

## 反射 API

复用式——这些名字是编译器自身对象的别名：

| 别名 | 真实对象 |
|---|---|
| `type`（`#fun` 的 `type` 参数） | `emperor.BoundType` |
| Field（`t.fields()` 元素） | `BoundClassFieldDefinition` |
| Method（`t.methods()` 元素） | `BoundFunctionDefinition` |
| Variant（`t.variants()` 元素） | `BoundEnumMemberDefinition` |

类型方法：`display_name()`、`kind_str()`、`is_class()`、`is_enum()`、`is_interface()`、`is_primitive()`、`is_value_type()`、`is_reference_type()`、`fields()`、`methods()`、`variants()`、`generic_args()`。Field/Method/Variant 对象以普通字段暴露数据（`name`、`bound_type`……）。没有注解系统。

类型令牌以 `emperor.BoundType` 引用进入 `#fun`；计算名字的动态解析经 `compiler().resolve_type`。

## #class

`#class` 声明仅元期存在的数据类：完整类能力，只编译进单元 B，绝不输出进运行时程序。元类可直接使用反射类型，不能返回进运行时代码。

## #compiler()

`#compiler()` 返回代理正在编译的编译器的 `CompilerContext`：

| 方法 | 效果 |
|---|---|
| `error(msg)` / `warn(msg)` / `info(msg)` | 发出编译期诊断（`E_META`） |
| `set_option(k, v)` / `get_option(k) -> string` / `has_option(k) -> bool` | 选项存储 |
| `resolve_type(name) -> type` / `resolve_symbol(name)` / `has_type(name) -> bool` | 解析 |
| `create_expression(text) -> ast` / `create_definition(text) -> ast` | 在元运行时解析源码文本 |
| `create_ast(...)` / `create_empty_ast()` / `create_function_ast(...)` | AST 构造 |
| `parse_arguments(text) -> ast` | 解析实参表（用于可变参数尾随块） |
| `get_ast(token) -> ast` | 物化已存的 AST 令牌 |
| `get_current_scope() -> string` | 类成员重写期间的包围类 |
| `can_compile_expression(t)` | 延迟（未实现） |

`#fun` 之外的 `#compiler()` 报 `E_UNSUPPORTED`。

## AST 参数与代码生成

最后参数为 `ast`（解析后）或 `unstructured_ast`（原始文本）的 `#fun` 接受尾随代码块：

```penguin
#fun tagged(tag: string, field: string, trailing_ast: unstructured_ast) -> ast {
    let probe: i64 = compiler().create_definition(trailing_ast);
    if (compiler().get_definition_kind(probe) != "class_field") {
        compiler().error("#tagged must annotate a field declaration");
    }
    return compiler().create_definition(
        "fun tag_of_" + field + "() -> string { return \"" + tag + "/" + field + "\"; } "
        + trailing_ast);
}

class C {
    #tagged("alpha")
    name: string = "field-ok";
}
```

* 表达式位置返回 `ast` 的元调用在调用点重新绑定返回的表达式。
* 定义位置返回定义的调用把它们拼接进去（像手写代码一样流过剩余各遍；拼接定义上盖调用文件的位置戳）。
* 可变参数宏：接收 `parse_arguments(text)` 的单个 `ast` 参数覆盖任意实参表。

## #specializing

`#specializing <Type><Args>` 块在每个泛型实例化时、单态化阶段运行一次。其体是编译期代码；体内的 `impl` 片段附属到特化类型：

```penguin
#template(N: i32)
class foo {
    impl IReferenceType;
}

#specializing foo<N> {
    if (N > 3) {
        impl IDescribe {
            fun describe(this) -> string { return "big"; }
        }
    } else if (N == 2) {
        impl IDescribe {
            fun describe(this) -> string { return "two"; }
        }
    } else {
    }
}
```

specializing 块内的 `#error("...")` 对该实例化中止编译。门控可以是 `#fun` 调用（JIT 求值）；单态化器读回激活的 impl 槽并注入。

## 额外元源

`#fun` 可以调用用户代码，前提是该文件被列为元源：`--meta-src file.penguin`（或工程文件中的 `meta-sources=[...]`）。元源逐字编译进单元 B，是编译期可见的唯一用户代码（显式清单、无重入）。

## 未实现

* 集合上的 `#for`（已解析；拼接展开逐步落地）。
* 类型值方法链（`T::method()` 风格）；`can_compile_expression`（探测类型能力）与构建其上的自定义约束库。
- 注解系统（字段/方法不携带供反射的属性元数据）。
- 编译期代码中的 `Map`。
