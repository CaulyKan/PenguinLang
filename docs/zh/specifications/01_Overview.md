# 概述

PenguinLang 是一门静态类型、带垃圾回收的编程语言，使用类 C 语法，内建协程与离散时序模型。它取材于 C（语法）、C#/Java（垃圾回收）、Rust（类型系统、枚举、接口）、Go（协程）与 Verilog/SystemC（并发与时序词汇）。

本章定义最小完整程序：入口、控制台输出、函数与变量声明。

## 程序结构

一个 PenguinLang 程序是一组源文件（`.penguin`）。执行从 **`initial` 块**开始，而非 `main` 函数。程序可含任意多个 `initial` 块；它们是调度器下并发运行的独立例程（见[异步与时序模型](./09_AsyncAndTimingModel.md)）：

```penguin
initial {
	println("hello world from penguin-lang!");
}
```

每个源文件还可以声明命名空间、函数、类、枚举、接口、全局变量与 `construct` 连线块（见[命名空间与工程](./08_NamespaceAndProject.md)与[模块化编程](./10_ModularProgramming.md)）。全局变量在仿真开始前按依赖序赋值；`construct` 块在 elaboration 阶段运行；随后每个 `initial` 例程被启动。

所有 initial 例程结束或调度器到达静默（所有例程停靠、无任何东西可唤醒任何作业）时程序终止——退出码 0。`exit(code)` 立即终止。

## 控制台输出

`print`/`println` 写 stdout（`eprint`/`eprintln` 写 stderr）；`println` 追加换行。参数是字符串：

```penguin
print("A");
println("B");                       // 输出 "AB\n"
println("n=" + cast<string>(42));   // n=42
```

`cast<string>(x)` 把任意基元转为字符串形式。原生运行时上 `std.io.print` 是同一函数（`std.io` 库见[命名空间与工程](./08_NamespaceAndProject.md)）。

## 函数

函数用 `fun name(params) -> ret { ... }` 声明：

```penguin
fun add(a: i32, b: i32) -> i32 {
	return a + b;
}

fun greet() {
	println("hello");
}
```

返回类型标注可省略，条件是函数体最后的表达式或 `return` 语句能确定它。最后一条语句是表达式的函数隐式返回该表达式。函数可为泛型（`#template`）、async、lambda 或值——完整规则见[函数](./04_Function.md)。

## 变量声明

变量用 `let` 以四种形式声明：

| 形式 | 含义 |
|---|---|
| `let x: T = v;` | 不可变绑定，不可变值 |
| `let x: mut T = v;` | 不可变绑定，**可变**值（`mut` 在类型上） |
| `let mut x = v;` | **可变绑定**，类型推断——不允许类型标注 |
| `let x: !mut T = v;` | 显式不可变值（与第一种形式相同） |

```penguin
let a: i32 = 1;        // a = 2 是编译错误
let b: mut i32 = 1;    // b = 2 合法
let mut c = 1;         // c = 2 合法；类型推断为 i32
let d: !mut i32 = 1;   // 显式不可变
```

`let mut x: i32 = 1;`（`let` 上的 mut *加* 类型标注）是编译错误。只有存在初始化器时才可省略类型。完整的可变性与赋值兼容规则——含成员、泛型与参数——定义在[数据类型](./03_DataTypes.md)。

声明也可以不带初始化器（`let b: mut i32;`），之后赋值。语言没有 `null`：缺失用 `Option<T>` 表达（`some`/`none`，见[枚举](./06_Enum.md)）。

## 词汇表

| 概念 | 位置 |
|---|---|
| 类型、可变性、值/引用语义 | [数据类型](./03_DataTypes.md) |
| 控制流语句与表达式 | [基本执行流](./02_BasicExecutionFlow.md) |
| 函数、lambda、生成器 | [函数](./04_Function.md) |
| 类 | [Class](./05_Class.md) |
| 枚举（`Option`、`Result`） | [枚举](./06_Enum.md) |
| 接口与调度 | [接口](./07_Interface.md) |
| 命名空间、工程、库 | [命名空间与工程](./08_NamespaceAndProject.md) |
| 协程与时序模型 | [异步与时序模型](./09_AsyncAndTimingModel.md) |
| 模块、端口、通道 | [模块化编程](./10_ModularProgramming.md) |
| 编译期代码 | [元编程](./11_MetaProgramming.md) |
