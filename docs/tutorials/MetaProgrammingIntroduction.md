# Meta Programming in EmperorPenguin

> PenguinLang 编译期元编程 —— 从简单到复杂，每个功能点对照主流语言（Rust / C++ / C# / Scala）
>
> Cauly Kan | Aug, 2026

## 00 什么是元编程？

**写程序的程序**：一段代码在编译期执行，计算数值、变换类型、生成代码，结果再进入正常编译流程。

- EmperorPenguin 把每个 `#fun` 用 **LLVM ORC JIT** 编译成原生代码，在编译过程中直接执行（native Pass2 / Pass3）。
- 执行结果（数值 / 类型 / AST 片段）**拼回编译管线**，继续走类型检查、IR 生成、LLVM 发射 —— 运行时零开销。
- 本文从最简单的「编译期开关」讲到最复杂的「代码生成（derive 宏）」；每个功能点选 1~2 个最有代表性的语言对照，其余语言用一句话说明。

## 01 设计理念：让元编程像写普通代码

PenguinLang 的元编程不是另一门语言，而是同一门语言的编译期运行。三条核心理念贯穿后面所有功能：

### 1. 编译器代码与运行时代码尽量等价

`#fun` 的语法与普通函数完全一致 —— 没有独立的模板语言：

```penguin
#fun fib(n: i64) -> i64 {
    if (n < 2) { return n; }
    return fib(n - 1) + fib(n - 2);   // 与普通函数完全相同的写法
}
// 去掉 # 前缀就是运行时函数 —— 同一门语言，编译期 / 运行时两处可用
```

- 元函数里的 `if` / `while` / 递归 / 调用其它函数都是普通写法，编译器用 LLVM ORC JIT 把它编译成原生代码执行。
- 对比：C++ 模板元编程是"另一门语言"，Rust 宏是 token 层面的另一套体系 —— PenguinLang 只学一次。

### 2. 用顺序代码代替 SFINAE / 模式匹配

C++ 的类型分派要写偏特化 / SFINAE / `enable_if`，Rust 要写 `where` 谓词 —— 都是"让编译器猜"的声明式技巧。PenguinLang 把它们写成按人类思维顺序执行的代码：

```penguin
#fun choose_comparator(t: type) -> type {
    if (t == #typeof(f32) || t == #typeof(f64)) { return #typeof(FloatComp); }
    if (t == #typeof(string))                    { return #typeof(StrComp); }
    return #typeof(DefaultComp);     // 顺序判断，一目了然
}
```

```cpp
// 同样的分派在 C++ 里是偏特化 + SFINAE 的叠罗汉
template <typename T, typename = void> struct Comp { using type = DefaultComp; };
template <typename T>
struct Comp<T, std::enable_if_t<std::is_floating_point_v<T>>> { using type = FloatComp; };
```

- `#if (T == i32)`、`#specializing { if (N > 3) ... }` 都是顺序分支 —— 读起来就是普通逻辑。
- 条件可以是任意元代码（调用 `#fun`、反射字段），而不是预定义的谓词组合。

### 3. 用户与编译器协同工作

编译器把内部世界开放给用户代码：`compiler()` 上下文、`type` 就是编译器的 `BoundType`、字段反射、`create_definition` 注入 —— 用户代码与编译器读**同一批对象**，互相配合，而不是依赖语言预置的魔法：

```penguin
// 用户提供意图（一行宏），编译器提供对象（当前类、字段），
// 用户代码完成生成 —— 双方合作，而非语言包办
class Person {
    name: string;
    age: i64;
    #impl_json_serializable();   // json 库：反射字段 → 生成 impl
}
```

- 需要什么能力，就用 `#fun` + 反射 + `create_ast` 自己造 —— 不等待语言添加注解 / 属性系统。
- 局限也透明：做不到的（如异构元组）直接用生成代码绕过去，而不是硬造语法糖。

## 02 功能全景：从简单到复杂

| # | 功能 | 是什么 | 典型对应物 |
| --- | --- | --- | --- |
| 1 | `#define` / `#defined` / `#option` | 编译期键值存储 | C 宏 `-D` / C# 编译符号 |
| 2 | `#if` / `#elif` / `#else` | 编译期条件（条件代码生成） | C++ `if constexpr` |
| 3 | `#while` | 编译期循环（循环展开） | C++ 模板递归 |
| 4 | `#fun` | 编译期执行的函数 | C++ `constexpr` / Rust `const fn` |
| 5 | `#fun -> type` | 类型级函数（类型即值） | C++ type traits |
| 6 | `#template` | 泛型（类型参数 + 值参数） | C++ / Rust 泛型 |
| 7 | 模板值参数 ↔ 元函数 | 值参数传给 `#fun`、值参数本身是元调用 | 非类型模板参数 + `constexpr` |
| 8 | `#specializing` | 按实例化参数选择实现（编译期校验） | C++ 模板偏特化 / Rust `where` |
| 9 | `#typeof(T)` | 类型查询 | C++ `decltype` / C# `typeof` |
| 10 | `t.fields()` 等 | 结构化反射 | C# `System.Reflection`（运行时） |
| 11 | `#class` | 编译期专用数据结构 | （无直接对应物） |
| 12 | `#compiler()` | 编译器上下文访问 | Rust proc_macro / C# Source Generator |
| 13 | `#fun -> ast` | AST 代码生成（derive） | Rust `#[derive]` / C# Source Generator |
| 14 | `unstructured_ast` | AST 原始文本捕获（尾随块） | Rust proc_macro `TokenStream` |

- 功能 1~3 是**硬编码的编译期结构**：编译器直接求值，不经过 JIT。
- 功能 4 开始是真正的 **JIT 元函数**：`#fun` 编译成原生代码执行。

## 03 编译期选项：#define / #defined / #option

最轻量的元编程入口：往编译期键值表里存、取数据，还能从命令行注入。

```penguin
#define("MODE", "release");
#define("VERSION", "1.2.0");

initial {
    #if (#defined("MODE")) {
        println("MODE=" + #option("MODE"));     // 编译期读取
    }
    println("ver=" + #option("VERSION"));       // ver=1.2.0
}
```

- `#define(key, value)` / `#defined(key)` / `#option(key)` 是**内置元函数**（可用同名 `#fun` 覆盖）。
- 命令行注入与 `#define` 共享同一存储：`penguin -DMODE=debug test.penguin`。
- 典型用途：平台分支、特性开关、版本号注入。

### 其他语言对应（代表：C/C++、C#）

```cpp
#define PI 3.14
#define MODE "release"
#ifdef MODE
    printf("MODE=%s", MODE);
#endif
// 命令行: gcc -DMODE=debug test.c
```

```csharp
#define MODE
#if MODE
    Console.WriteLine("MODE");
#endif
// MSBuild: <DefineConstants>MODE</DefineConstants>
```

> Rust 用 `cfg!(feature = ...)` / `env!("VAR")` 近似；Scala 无编译期键值存储（由构建工具 sbt 的设置注入）。

## 04 编译期条件：#if / #elif / #else

根据编译期可求值的条件只保留选中分支，其余分支**不会进入后续编译阶段**。

### 顶层用法：控制整个定义的生成

```penguin
#define("X", "1");

#if (#defined("X") || #defined("Y")) {     // 支持 || / && / ! 逻辑运算
    fun flag() -> i64 { return 1; }
} #elif (#defined("Y")) {
    fun flag() -> i64 { return 2; }
} #else {
    fun flag() -> i64 { return 0; }
}
```

### 类型条件分支（模板内）

与顶层用法相同，条件可以是类型比较

```penguin
#template(T: type)
fun default_value() -> T {
    #if (T == i32) {
        return 0;
    } #elif (T == f32) {
        return 0.0;
    } #else {
        return T.default();
    }
}
```

- `#if` 是**硬编码编译期结构**：编译器直接求值条件，不经过 JIT。
- **注意**：分支关键字是 `#elif` / `#else`（带 `#`），写成普通 `else if` 会被解析成运行时嵌套 if。

### 其他语言对应（代表：C++、Scala）

```cpp
template <typename T>
T default_value() {
    if constexpr (std::is_same_v<T, int>)        return 0;
    else if constexpr (std::is_same_v<T, float>) return 0.0f;
    else                                         return T{};
}
```

```scala
inline def classify(x: Int): String =
  inline if (x > 3) then "big"
  else if (x == 2) then "two"
  else "other"
// 实参为编译期常量时，只保留选中分支
```

> Rust 无 `if constexpr`（用 `cfg!` / 泛型特化）；C# 预处理器 `#if` 只能按编译符号分支。

## 05 编译期循环：#while

`#while` 在编译期反复求值条件、展开循环体，展开后的代码才进入后续编译。

```penguin
#fun count_bits(v: u32) -> u32 {
    let bits: mut u32 = 0;
    let remaining: mut u32 = v;
    #while (remaining > 0) {
        bits = bits + (remaining & 1);
        remaining = remaining >> 1;
    }
    return bits;
}

initial {
    println("bits=" + cast<string>(#count_bits(255)));   // 8，编译期算出
}
```

- `#while` 与 `#if` 一样是硬编码编译期结构：编译器直接求值条件并展开。
- `#for (let i in range(0, N)) { ... }` 与编译期 `#break` / `#continue` 已进入解析器，splice 展开陆续落地中。
- 展开结果零运行时开销；适合编译期查表、序列展开。

### 其他语言对应（代表：C++、Rust、Scala）

```cpp
template <int N>
struct Sum {
    static constexpr int value = Sum<N-1>::value + N;
};
template <> struct Sum<0> { static constexpr int value = 0; };
```

```rust
const fn sum_upto(n: usize) -> usize {
    let mut s = 0;
    let mut i = 0;
    while i <= n { s += i; i += 1; }
    s
}
const S: usize = sum_upto(10);   // 编译期求值
```

```scala
// 编译期循环：inline 递归展开
inline def sumUpTo(inline n: Int): Int =
  inline if (n <= 0) then 0 else n + sumUpTo(n - 1)

val s: Int = sumUpTo(5)   // 编译期展开 → 15
```

> C# 无编译期循环（只能交给 T4 代码生成器）。

## 06 元函数：#fun

`#fun` 在编译期被 LLVM ORC JIT 编译成原生代码并执行，结果直接拼回调用点。

```penguin
#fun fib(n: i64) -> i64 {
    if (n < 2) { return n; }
    return fib(n - 1) + fib(n - 2);   // 递归调用不需要 # 前缀
}

initial {
    let x: i64 = #fib(10);
    println("fib=" + cast<string>(x));   // 等价于 let x: i64 = 55;
}
```

- `#fun` 只能在**全局 / 命名空间作用域**声明；返回类型不能为引用类型。
- 返回类型可以是普通值（`i64` / `bool` / `string` ...）、`type`、`ast`。
- 按需编译 + 缓存；体内递归、循环、调用其他 `#fun` 都是普通写法。

### 其他语言对应（代表：C++、Rust、Scala）

```cpp
constexpr int fib(int n) {
    return n < 2 ? n : fib(n - 1) + fib(n - 2);
}
static_assert(fib(10) == 55);   // 编译期计算
```

```rust
const fn fib(n: u64) -> u64 {
    if n < 2 { n } else { fib(n - 1) + fib(n - 2) }
}
const FIB_10: u64 = fib(10);    // 编译期计算
```

```scala
inline def square(x: Int): Int = x * x
inline def twice(x: Int): Int = square(x) + square(x)

val n: Int = twice(21)   // 编译期展开求值
```

> C# 无「把同一函数在编译期求值」的能力（Source Generator 在独立生成器进程中运行）；Scala 3 的 `inline def` 在调用点编译期展开求值。

## 07 类型级函数：#fun -> type

`type` 是合法的参数 / 返回类型：元函数可以**直接计算出一个类型**，用在类型位置。

```penguin
#fun signed_to_unsigned(t: type) -> type {
    if (t == #typeof(i32)) { return #typeof(u32); }
    if (t == #typeof(i64)) { return #typeof(u64); }
    return #typeof(void);   // 其他类型走默认分支
}

initial {
    let x: #signed_to_unsigned(i32) = 0;   // x 的类型是 u32
}

#template(T: type)
fun abs_val(v: T) -> #signed_to_unsigned(T) {
    return cast<#signed_to_unsigned(T)>(v);
}
```

- 对比 C++：不需要偏特化或 trait，一个普通 `if` 就完成类型分派。
- 元函数内部可以随意操作类型：比较、传参、返回。

### 其他语言对应（代表：C++、Scala）

```cpp
template <typename T> struct make_unsigned;
template <> struct make_unsigned<int>  { using type = unsigned int; };
template <> struct make_unsigned<long> { using type = unsigned long; };
using T = make_unsigned<int>::type;      // 标准库: std::make_unsigned_t<T>
```

```scala
type ToUnsigned[X] = X match
  case Int  => Long
  case Long => BigInt

val x: ToUnsigned[Int] = 5L   // 类型级函数：编译期计算
```

> Rust 用 trait + associated type 模拟（`type Output`）；C# 无（`typeof(T)` 不能参与编译期类型计算）。Scala 3 的 `match types` 是原生的类型级函数。

## 08 泛型模板：#template

`#template` 是「返回类型的元函数」的语法糖，同时支持**类型参数**和**值参数**（编译期常量）。

### 类型参数

```penguin
#template(T: type)
class Box<T> { value: T; }

#template(T: type)
fun identity(v: T) -> T { return v; }

initial {
    let b: Box<i32> = new Box<i32>();
    let x = identity<string>("hi");
}
```

- 每次实例化都会**单态化**（如 `Box<i32>`），无虚函数开销。
- 概念上等价于 `#fun Box(T: type) -> type { ... }`。

### 值参数

数字、布尔、字符串都可以作为值参数：

```penguin
#template(N: i32)                  // 数字值参数
fun dbl() -> i64 { return N * 2; }

#template(B: bool)                 // 布尔值参数
fun flag() -> i64 {
    if (B) { return 1; }
    return 0;
}

#template(S: string)               // 字符串值参数
fun slen() -> i64 {
    return string_length(S);
}

initial {
    println(cast<string>(dbl<5>()));          // 10
    println(cast<string>(flag<true>()));      // 1
    println(cast<string>(slen<"hello">()));   // 5
}
```

### 其他语言对应（代表：C++、Rust、Scala）

```cpp
template <typename T> struct Box { T value; };
template <unsigned N> unsigned dbl() { return N * 2; }
```

```rust
struct Box<T> { value: T }
fn dbl<const N: u32>() -> u32 { N * 2 }
```

```scala
import scala.compiletime.constValue

class Box[T](val value: T)

// 值参数：字面量类型 N + constValue 取回常量
inline def dbl[N <: Int]: Int = constValue[N] * 2

val b = new Box[Int](42)
val n = dbl[5]   // 10，编译期求值
```

> C# 只有类型参数（值参数几乎不可用）。

## 09 模板值参数与元函数交互：#template + #fun

值参数在特化时替换进函数体 / 字段初值；如果替换后的代码里含有 `#fun` 元调用，元调用会随特化一起在编译期求值 —— **单态化与元求值的组合**。

### 值参数传给元函数

```penguin
#fun test(i: i64) -> i64 { return i * 2; }

#template(N: i32)
fun foo() -> i64 {
    return #test(N);      // 特化时 N -> 5，得到 #test(5)
}

initial {
    let r = foo<5>();     // 特化 foo__5，元调用编译期求值 -> 10
    println("r=" + cast<string>(r));
}
```

### 值参数本身可以是元函数调用

值参数不限于字面量：`new A<#compute_n()>()` 中的元调用会在特化前 JIT 求值（native Pass3）。

```penguin
#fun compute_n() -> i64 { return 5; }

#template(N: i32)
class A {
    foo: i32 = N;
}

initial {
    let a = new A<#compute_n()>();   // 值参数 = 编译期表达式
    println("foo=" + cast<string>(a.foo));   // 5
}
```

- 混用：`#template(T: type, N: i32)` + `new P<i32, #compute_n()>()` —— 类型参数与值参数按位置分别解析。
- 特化体里的 `#test(N)` 被求值并拼接为常量 —— 模板展开时自动触发元求值，无需额外步骤。

### 其他语言对应（代表：C++、Rust、Scala）

```cpp
constexpr unsigned test(unsigned i) { return i * 2; }

template <unsigned N>
unsigned foo() { return test(N); }   // constexpr 求值
// C++20 起: template<auto N> / consteval
```

```rust
const fn test(i: u64) -> u64 { i * 2 }

fn foo<const N: u64>() -> u64 { test(N) }   // const 上下文求值
```

```scala
import scala.quoted.*
import scala.compiletime.constValue

// 值参数 N 传给 macro（相当于 #template(N) + #test(N)）
inline def foo[N <: Int]: Int = ${ fooImpl[N] }

def fooImpl[N <: Int](using Quotes): Expr[Int] =
  Expr(constValue[N] * 2)   // 编译期计算

val r: Int = foo[5]   // 10
```

> C# 泛型没有值参数。

## 10 模板特化：#specializing

按实例化参数控制一个类**是否实现某个接口**、以及**如何实现**，条件是**编译期元代码** —— 是 Rust `impl ... where` 子句的正面、声明式替代（C++ / Scala 的对应机制见下）。

```penguin
#template(T: type)
interface IDescribe {
    fun describe(this) -> string;
}

#template(N: i32)
class foo {
    impl IReferenceType;
}

#specializing foo<N> {
    if (N > 3) {
        impl IDescribe {
            fun describe(this) -> string { return "big"; }
        }
    }
    else if (N == 2) {
        impl IDescribe {
            fun describe(this) -> string { return "two"; }
        }
    }
    else if (N == 1) {
        #error("N never 1");     // 实例化 foo<1> 时编译失败
    }
}

initial {
    let a = new foo<4>();
    let da: IDescribe = a;
    println("n=" + da.describe());   // "big"
}
```

- 条件在**实例化时**求值：`foo<4>` 走第一个分支，`foo<2>` 走第二个。
- `#error("...")` 使编译失败 —— 比 `static_assert` 更灵活：条件可以是任意元代码，且无需手动维护常量。
- 每个分支可以注入不同的 `impl` / 方法 —— 既决定**是否实现**，也决定**如何实现**。

### 其他语言对应（代表：Rust、C++、Scala）

```rust
// 条件实现：where 子句决定「是否实现」以及「如何实现」
trait IDescribe { fn describe(&self) -> String; }

struct Foo<T>(T);

trait NotJson {}                    // 约束标记
impl NotJson for i32 {}
impl NotJson for String {}

impl<T: NotJson> IDescribe for Foo<T> {
    fn describe(&self) -> String { "big".to_string() }
}

let f = Foo(42i32);                 // i32: NotJson → 有实现
// Foo<f64> 没有 NotJson → 没有 IDescribe，用作接口即编译错误
```

```cpp
// 特化决定「是否继承接口」以及「如何实现」
template <int N> struct Foo : IDescribe {
    std::string describe() const override { return "big"; }
};
template <> struct Foo<2> : IDescribe {
    std::string describe() const override { return "two"; }
};
template <> struct Foo<1> {};      // N==1：不实现接口，用作 IDescribe 即编译错误
```

```scala
// 类型类 + given：编译期实例搜索决定「是否具有能力」
trait Describe[T]:
  def describe: String

case class Foo[N <: Int]()

given big4: Describe[Foo[4]] = new Describe[Foo[4]]:
  def describe = "big"
given two2: Describe[Foo[2]] = new Describe[Foo[2]]:
  def describe = "two"

val s = summon[Describe[Foo[4]]].describe    // "big"
// summon[Describe[Foo[1]]] —— 编译错误：找不到 given 实例
```

> 对比：penguin 的条件是任意编译期元代码（可调用 `#fun`，如 json 库的 `json_deserializable_type(T)`），Rust / C++ / Scala 的约束则是类型或常量谓词；C# 泛型无法按值分派。

## 11 类型查询：#typeof(T)

`#typeof(T)` 把类型名解析成 `type` 值，交给元函数做比较、反射、传递。

```penguin
#fun is_integer(t: type) -> bool {
    return t == #typeof(i32) || t == #typeof(i64);
}

#template(T: type)
fun check() {
    #if (is_integer(#typeof(T))) {
        println("T is an integer type");
    } #else {
        println("T is not an integer type");
    }
}

initial {
    let a: #typeof(i32) = 42;      // 直接当类型用
    check<i32>();                  // "T is an integer type"
    check<string>();               // "T is not an integer type"
}
```

- 模板内 `#typeof(T)` 沿作用域链解析到**具体实例化类型**。
- 反射入口：把 `#typeof(Point)` 传给元函数，即可拿到类型的字段 / 方法（见下节）。

### 其他语言对应（代表：C++、C#、Scala）

```cpp
decltype(x)                  // 表达式的类型
std::is_same_v<T, int>       // 编译期类型比较
```

```csharp
typeof(T)                    // 编译期求得 System.Type 对象
typeof(T).GetFields()        // 反射字段 —— 只能发生在运行时
```

```scala
import scala.compiletime.erasedValue

// 编译期类型查询：erasedValue match 按类型分派
inline def isInt[T]: Boolean =
  inline erasedValue[T] match
    case _: Int    => true
    case _         => false

val b: Boolean = isInt[Int]      // true
val c: Boolean = isInt[String]   // false
```

> Rust 有 `std::any::type_name::<T>()`（编译期类型名）与 `TypeId::of::<T>()`。C# 的 `typeof(T)` 虽在编译期求值，但用它反射字段 / 方法发生在**运行时** —— 不是编译期元编程。

## 12 结构化反射：t.fields() / t.methods() / t.variants()

`type` 就是编译器的 `BoundType` 本体：`fields()` / `methods()` / `variants()` 返回编译器**正在使用中的对象**，字段直接读。

### 读取字段列表

```penguin
class Point {
    x: mut i32;
    y: mut i32;
}

#fun field_count_of(t: type) -> i64 {
    return cast<i64>(t.fields().size());
}

initial {
    println("count=" + cast<string>(#field_count_of(#typeof(Point))));   // 2
}
```

### 遍历字段名

字段对象直接暴露 `name` / `bound_type`，配合 `#while` 逐字段处理：

```penguin
#fun describe(t: type) -> string {
    let fs = t.fields();
    let mut s = t.display_name() + "{";
    let i: mut i64 = 0;
    #while (i < cast<i64>(fs.size())) {
        if (i > 0) { s = s + ", "; }
        s = s + fs.at(cast<u64>(i)).some.name;   // 直接读 Field.name
        i = i + 1;
    }
    return s + "}";
}

initial {
    println(#describe(#typeof(Point)));   // Point{x, y}
}
```

- 没有平行的 FieldInfo / MethodInfo 体系 —— 元代码与编译器读的是**同一批对象**。
- 类型方法：`display_name()` / `kind()` / `is_class()` / `fields()` / `methods()` / `variants()` / `generic_args()`；`methods()` / `variants()` 与 `fields()` 同构。

### 其他语言对应（代表：C#、Scala）

```csharp
typeof(Point).GetFields();   // 运行时反射：编译后才知道字段，且带运行时开销
```

```scala
case class Point(x: Int, y: Int) derives JsonCodec
// derives 在编译期生成 impl；
// Mirror 在编译期提供字段名（MirroredElemNames）与类型元数据
```

> **注意**：C# 要达到类似效果只能靠 **System.Reflection 运行时反射** —— 发生在运行时，**不是编译期元编程**；要在编译期拿到字段列表并生成代码，只能靠 Source Generator（见第 14 / 15 节）。C++ 无（C++26 静态反射提案未落地，`magic_enum` 等库靠解析编译器内建字符串）；Rust 无（proc macro 只能看到 TokenStream）；Scala 3 的 `derives` + `Mirror` 是编译期结构反射（字段名 / 类型以类型元数据形式暴露）。

## 13 元数据类：#class

`#class` 是仅存在于元编译单元的数据结构：给 `#fun` 当编译期中间数据用，**不会**进入最终程序。

```penguin
#class Acc {
    total: mut i64;
    fun new(mut this) { this.total = 0; }
    fun add(mut this, x: i64) { this.total = this.total + x; }
    fun get(this) -> i64 { return this.total; }
}

#fun use_class(n: i64) -> i64 {
    let a: mut Acc = new Acc();    // #class 实例只能在元函数里使用
    a.add(n);
    a.add(n);
    return a.get();
}

initial {
    println("result=" + cast<string>(#use_class(21)));   // 42
}
```

- 字段 / 方法 / 泛型 / 可变性 —— 与普通 class 能力完全一致。
- 适用场景：编译期收集数据、构造生成器需要的中间表示。

### 其他语言对应（代表：C++、Scala）

```cpp
template <typename T> struct TypeTraits;   // 编译期数据载体
struct TrueType  { static constexpr bool value = true; };
struct FalseType { static constexpr bool value = false; };
```

```scala
// 编译期中间数据：普通 case class（inline 展开时求值）
case class Acc(total: Int)

inline def use(inline n: Int): Int =
  Acc(0).copy(total = n + n).total

val r: Int = use(21)   // 编译期求值 → 42
```

> Rust 无直接对应（const fn 内可用普通 struct 做临时数据）；C# 靠生成器进程内的辅助类。

## 14 编译器上下文：#compiler()

`#compiler()` 返回编译器代理对象：查类型 / 符号、解析代码、输出诊断。

### 诊断与校验

```penguin
#fun assert_small(t: type) -> i64 {
    let n = cast<i64>(t.fields().size());
    #if (n > 3) {
        compiler().error("Type " + t.display_name() + " has too many fields");
    } #else {
        compiler().info("OK: " + t.display_name());
    }
    return n;
}

initial {
    let n = assert_small(#typeof(Point));   // 输出 "OK: Point"
}
```

- 诊断：`error`（使编译失败）/ `warn` / `info`。
- 选项：`set_option` / `get_option` / `has_option`（与 `#define` 同一存储）。

### 查询与解析

```penguin
#fun lookup_option() -> string {
    let opt = compiler().resolve_type("Option");    // Option<type>
    if (opt.is_none()) { compiler().error("Option not found"); }
    return opt.some.display_name();
}

initial {
    println("t=" + lookup_option());   // t=Option
}
```

- 查询：`resolve_type` / `resolve_symbol` / `has_type`。
- 解析：`create_expression` / `create_definition` / `create_ast`（文本 → AST）。

### 其他语言对应（代表：Rust、C#、Scala）

```rust
compile_error!("too many fields");     // 或 proc_macro::Diagnostic::spanned_error
```

```csharp
context.ReportDiagnostic(Diagnostic.Create(rule, location, "too many fields"));
// GeneratorExecutionContext 提供语法树 / 语义模型查询
```

```scala
import scala.quoted.*

// 编译期诊断：macro 在 Quotes 上下文里报错
inline def assertSmall(inline n: Int): Int = ${ assertSmallImpl('n) }

def assertSmallImpl(n: Expr[Int])(using Quotes): Expr[Int] =
  import quotes.reflect.report
  n.value match
    case Some(v) if v > 3 => report.errorAndAbort("too many fields")
    case _                => n
```

> C++ 只有 `static_assert` 诊断、没有查询 / 生成能力。

## 15 AST 代码生成：#fun -> ast

元函数返回 `ast` 时，结果会被**拼回调用点**继续走正常编译管线 —— 这就是 derive 宏。

### 生成与注入

```penguin
class Point { x: mut i32; y: mut i32; }

#fun derive_clone(t: type) -> ast {
    let fs = t.fields();
    let n = cast<i64>(fs.size());
    let mut body = "fun my_clone(p: mut Point) -> Point { let q: mut Point = new Point(); ";
    let i: mut i64 = 0;
    #while (i < n) {
        let fname = fs.at(cast<u64>(i)).some.name;
        body = body + "q." + fname + " = p." + fname + "; ";
        i = i + 1;
    }
    body = body + "return q; }";
    return compiler().create_definition(body);    // 文本 → 定义 AST
}

#derive_clone(#typeof(Point));    // 生成并注入 my_clone()
```

组合拳：反射字段 → 拼接源码 → `create_definition` → 注入 → 正常编译。

### 使用生成的函数

```penguin
initial {
    let p: mut Point = new Point();
    p.x = 3; p.y = 4;
    let q: Point = my_clone(p);   // 生成的函数直接可用
    println(cast<string>(q.x) + "," + cast<string>(q.y));   // 3,4
}
```

- **尾随代码块**：若 `#fun` 最后一个参数是 `ast`，调用点可跟一个 `{ ... }` 代码块作为该参数；`unstructured_ast` 则捕获原始文本。
- 生成代码与手写代码地位相同：类型检查、IR、LLVM 全流程照常。

### 其他语言对应（代表：Rust、C#、Scala）

```rust
#[proc_macro_derive(Clone)]
pub fn derive_clone(input: TokenStream) -> TokenStream {
    // 解析字段 → 生成代码 → 返回 TokenStream
}

#[derive(Clone)]
struct Point { x: i32, y: i32 }
```

```csharp
[Generator]
public class CloneGen : ISourceGenerator {
    // 用语法树 / 语义模型读字段，向 context 添加生成文件
}
```

```scala
import scala.quoted.*

inline def printf(fmt: String, args: Any*): String =
  ${ printfImpl('fmt, 'args) }      // 引号代码拼接

def printfImpl(fmt: Expr[String], args: Expr[Seq[Any]])(using Quotes): Expr[String] =
  // 编译期检查格式串并生成表达式（类似 penguin 的 #printf）
```

> C++ 用宏 / X-macro / 外部代码生成器（protoc 等）。

## 16 AST 原始文本捕获：unstructured_ast

`ast` 参数捕获**结构化 AST 节点**；`unstructured_ast` 则按**原始文本**捕获（不做解析、逗号保留），交给元函数自己处理 —— 适合变长参数、格式串等需要看到原始写法的场景。

```penguin
#fun printf(fmt: string, params: unstructured_ast) -> ast {
    let token = compiler().parse_arguments(params);   // 原始文本 -> 实参列表 AST
    let expr = compiler().get_ast(token);             // 取真实节点做内省
    if (expr is emperor.Expression.function_call_arguments) {
        let args = expr.function_call_arguments;
        let n = cast<i64>(args.size());
        let mut fmt_idx = 0;
        let mut arg_idx = 0;
        let fmt_len = string_length(fmt);
        let mut result = "println(\"";
        while (fmt_idx < fmt_len) {
            let ch = string_char_at(fmt, fmt_idx);
            if (ch == "{" && fmt_idx + 1 < fmt_len && string_char_at(fmt, fmt_idx + 1) == "}") {
                if (arg_idx < n) {
                    let arg_expr = args.at(cast<u64>(arg_idx)).some;
                    result = result + "\" + cast<string>(" + arg_expr.build_text() + ") + \"";
                }
                arg_idx = arg_idx + 1;
                fmt_idx = fmt_idx + 2;
            } else {
                result = result + ch;
                fmt_idx = fmt_idx + 1;
            }
        }
        result = result + "\")";
        return compiler().create_expression(result);
    }
    return compiler().create_expression("println(\"error\")");
}

initial {
    let a: i32 = 10;
    let b: i32 = 20;
    #printf("a={}, b={}") { a, b };   // 尾随块按原始文本捕获: "a , b"
    // 生成: println("a=" + cast<string>(a) + ", b=" + cast<string>(b));
}
```

- 尾随 `{ ... }` 块以原始文本送达（如 `"a , b"`，逗号原样保留）。
- `compiler().parse_arguments` 把原始文本解析成 `FunctionCallArguments` 节点；`compiler().get_ast` 取回真实节点做内省 —— 这里逐实参生成 `cast<string>(...) + ...` 的拼接表达式。

### 其他语言对应（代表：Rust、C#）

```rust
// proc_macro 收到的是原始 TokenStream（类似 unstructured_ast）
#[proc_macro]
pub fn printf(input: TokenStream) -> TokenStream {
    // 需要自己把 TokenStream 解析成 TokenTree 或 AST
}
```

```csharp
// Source Generator 拿到的是已解析的语法树（类似 ast，结构化）
// SyntaxNode / ArgumentList —— 无需自行解析文本
```

> C++ 宏收到的是预处理后的原始 token；Scala 3 的引号代码 `'{...}` 是结构化 `Expr`，没有原始文本概念。

## 17 模板中的容器：List&lt;T&gt; vs C++ tuple

模板里同样可以使用标准容器：penguin 的 `List<T>` 在单态化后成为**具体类型**的运行时链表；C++ 侧对应的「模板 + 容器」场景是变长参数包 + `std::tuple` —— 元素类型可以各不相同，数量在编译期固定。

```penguin
#template(T: type)
class ShoppingCart {
    items: mut List<T>;            // 模板里的 List：实例化后是具体类型链表

    fun new(mut this) { this.items = new List<T>(); }
    fun add(mut this, item: T) { this.items.push(item); }
    fun count(this) -> i64 { return cast<i64>(this.items.size()); }
}

initial {
    let cart = new ShoppingCart<string>();   // 单态化：items 是 List<string>
    cart.add("apple");
    cart.add("banana");
    println("count=" + cast<string>(cart.count()));   // 2
}
```

```cpp
#include <tuple>
#include <utility>

template <typename... Ts>
class Cart {
    std::tuple<Ts...> items;        // 异构集合：每个元素类型可以不同
public:
    explicit Cart(Ts... vs) : items(std::move(vs)...) {}
    std::size_t count() const { return sizeof...(Ts); }   // 编译期固定
};

// 用法：每种元素一个类型槽位
Cart<int, std::string> c(2, "apple");
```

- penguin：`#template(T: type)` 实例化后 `items` 的类型完整确定（如 `List<i32>`），`push` / `size` 等成员直接可用 —— 单态化让容器类型完整落地。
- C++：`Ts...` 是变长模板参数包，`std::tuple<Ts...>` 的元素类型可以各不相同，数量在编译期固定（`sizeof...(Ts)`）。
- 对比要点：「同质、运行时可变」的 `List<T>` vs 「异构、编译期固定」的 tuple —— 都无虚函数开销。
- penguin 没有内置异构元组：需要异构集合时，用 `#fun` + `ast` 在编译期生成对应代码（见第 15 节）。

## 18 综合示例（真实项目）：JSON 标准库的实现

`EmperorPenguin/std/penguin/json.penguin`（888 行）是标准库里真实使用元编程的例子：序列化接口、按字段反射自动生成 `impl`、条件特化。它提供两层 API：

- **动态 API**：`JsonValue`（带标签枚举，数字以原文字符串保存以支持 round-trip）、`parse_json`、`JsonWriter` —— 不需要元编程。
- **元编程 API**：类里写一行 `#impl_json_serializable();`，编译期自动生成该类的序列化 / 反序列化 `impl`。

### 用法：一行宏，任意类可序列化

```penguin
class Person {
    name: string;
    age: i64;
    friends: std.Vector<string>;

    #impl_json_serializable();   // 编译期生成 impl std.IJsonSerializable<Person>
}

initial {
    let p = new Person();
    p.name = "Alice";
    p.age = 30;
    let json = p.json_serialize();              // {"name":"Alice","age":30,...}
    let q = Person.json_deserialize(json);      // 静态调用
}
```

### 实现拆解：反射 → 逐字段生成 → 注入

宏体就是几个普通 `#fun`：先拿到**当前类**的类型对象并反射字段，再按字段类型拼接源码，最后把整个 `impl` 注入回编译管线。

```penguin
#fun impl_json_serializable() -> ast {
    let t: emperor.BoundType = compiler().get_current_scope();   // 当前类
    if (t.display_name() == "void") {
        compiler().error("must be called inside a class body");
    }
    let cls: string = t.display_name();
    let fs = t.fields();                                        // 反射全部字段
    let mut ser = "let mut w = new std.JsonWriter(); w.begin_object(); ";
    let mut de  = "let _v: std.JsonValue = std.parse_json(json); let mut _obj = new " + cls + "(); ";
    let i: mut i64 = 0;
    while (i < cast<i64>(fs.size())) {
        let f = fs.at(cast<u64>(i)).some;
        ser = ser + "w.key(\"" + f.name + "\"); "
                  + json_write_snippet(f.bound_type, "this." + f.name) + "; ";
        de = de + "if (let _f := _v.get(\"" + f.name + "\").some) { "
                + json_read_stmt(f.bound_type, "_f", "_obj." + f.name) + " } ";
        i = i + 1;
    }
    ser = ser + "w.end_object(); return w.to_string();";
    de = de + "return _obj;";
    return compiler().create_definition(                        // 拼接后注入
        "impl std.IJsonSerializable<" + cls + "> { "
        + "fun json_serialize(this) -> string { " + ser + " } "
        + "fun json_deserialize(json: string) -> mut " + cls + " { " + de + " } "
        + "}");
}
```

字段的「怎么写 / 怎么读」由按类型分派的元函数生成 —— 容器字段递归展开，嵌套类字段委托给其自身的 `json_serialize`：

```penguin
#fun json_write_snippet(t: type, arg: string) -> string {
    let dn: string = t.display_name();
    if (string_find(dn, "std.Vector") >= 0 && t.generic_args.size() >= 1) {
        let elem = arg_as_bound_type(t.generic_args.at(0).some);
        if (json_serializable_type(elem) == false) { return ""; }   // 不可序列化则跳过
        return "w.begin_array(); for (let _v in " + arg + ") { "
             + json_write_snippet(elem, "_v") + " } w.end_array()";
    }
    // std.HashMap -> begin_object + 遍历 iter_keys()，同理
    if (json_serializable_type(t) == false) { return ""; }
    return "w.value_raw(" + arg + ".json_serialize())";
}

#fun json_serializable_type(t: type) -> bool {
    if (t.is_primitive()) { return true; }
    // Vector / HashMap / Option / Box：递归检查泛型参数
    // 类 / 枚举：检查是否实现了 IJsonSerializable（has_interface）
    return false;
}
```

### #specializing：只有元素可序列化时，Option&lt;T&gt; 才有 impl

```penguin
#specializing __builtin.Option<T> {
    if (json_deserializable_type(T)) {      // 实例化时求值的编译期条件
        impl std.IJsonSerializable<__builtin.Option<T>> {
            fun json_serialize(this) -> string {
                if (this is __builtin.Option<T>.some) { return this.some.json_serialize(); }
                return "null";
            }
            fun json_deserialize(json: string) -> mut __builtin.Option<T> {
                if (json == "null") { return new __builtin.Option<T>.none(); }
                let _v = std.parse_json(json);
                return new __builtin.Option<T>.some(#json_read_expr_ast(T, "_v"));
            }
        }
    }
}
```

- 正面语义：`Option<T>` 只有当 `T` 本身可反序列化时才获得 impl，否则把 `Option<T>` 当序列化类型使用就是编译错误 —— 类似 Rust 的 trait bound。反序列化体里的 `#json_read_expr_ast(T, "_v")` 是元调用拼接的表达式（第 9 节）。
- `Box<T>` 同理（`this.value.json_serialize()` / 拆包 `#json_read_expr_ast`）。

### 用到的功能一览与对照

- `#template` 接口 `IJsonSerializable<T>` + 全基础类型 impl（第 8 节）；`#fun` 生成源码片段（第 6 节）。
- `compiler().get_current_scope()` 取当前类 + `t.fields()` 反射字段（第 12 / 14 节）；`compiler().create_definition` 注入生成代码（第 15 节）。
- `#specializing` 条件实现（第 10 节）+ 特化时元函数求值（第 9 节）。
- 已知限制：目前仅支持具体顶层类（泛型参数字段类型无法在 codegen 分派），u64 / f64 的精度保留也有限制 —— 详见 json.penguin 头部注释。

### 其他语言对应（代表：Rust、Scala）

```rust
#[derive(Serialize, Deserialize)]
struct Person { name: String, age: i32 }
// serde derive：反射字段生成 impl
```

```scala
case class Person(name: String, age: Int) derives JsonCodec
// 编译期生成序列化 impl —— 与 #impl_json_serializable() 同一思路
```

> C# `System.Text.Json` 用运行时反射（非编译期）；C++ nlohmann/json 靠模板与特化。PenguinLang 的反射对象是编译器自己的 `BoundType`，宏体就是普通 `#fun` 代码。

## 19 能力对比一览

| 能力 | PenguinLang | C++ | Rust | C# | Scala 3 |
| --- | --- | --- | --- | --- | --- |
| 编译期计算 | `#fun`（JIT，原生速度） | `constexpr` | `const fn` | 无 | `inline def` |
| 编译期条件 | `#if` / `#elif` / `#else` | `if constexpr` / 预处理 | `cfg!` / 特化 | 编译符号 | `inline if` |
| 编译期循环 | `#while` / `#for` | 模板递归 | const fn 循环 | 无 | `inline match` + 递归 |
| 类型级函数 | `#fun -> type` | type traits | trait + assoc type | 无 | `match types` |
| 结构化反射 | `t.fields()` / `t.methods()` | 无（C++26 提案中） | 无 | System.Reflection（运行时，非编译期） | `derives` / `Mirror`（编译期） |
| 代码生成 | `#fun -> ast` | 宏 / 外部工具 | proc_macro | Source Generator | `macro`（引号代码） |
| 泛型值参数 | `#template(N: i32)` | `template<int N>` | const 泛型 | 无 | 字面量类型 + `constValue` |
| 编译期诊断 | `compiler().error()` | `static_assert` | `compile_error!` | 生成器 Diagnostic | `report.errorAndAbort` |
| 模板条件特化 | `#specializing` | 偏特化（决定是否继承接口） | `impl ... where` 条件实现 | 无 | given / 类型类实例 |
| 值参数 ↔ 元函数 | `#template(N)` + `#fun` | 非类型参数 + `constexpr` | const 泛型 | 无 | `inline` + `constValue` |
| AST 原始文本 | `unstructured_ast` | 宏（原始 token） | proc_macro `TokenStream` | 语法树（结构化） | 无（引号代码为结构化 Expr） |
| 概念数量 | **1 套统一模型** | 4+ 套机制 | 多种机制 | 生成器 API | 多套机制（inline / match types / macro） |

## 20 运行前提与参考资料

- 元编程 JIT 只在 **native Pass2 / Pass3** 生效：先执行 `make bootstrap` 构建自举编译器。
- Pass1 / BabyPenguin 只验证 `.penguin` 能编译，不执行元函数 JIT。
- 详细规范：[10_MetaProgramming.md](../specifications/10_MetaProgramming.md)。
- 可运行用例：`Tests/MetaProgramming/*.md`（约 80 个）。

```bash
dotnet run --project Tests/PenguinTestRunner -- --filter MetaProgramming/*
```
