# PenguinLang 基础入门

PenguinLang 是一门静态类型、带垃圾回收的编程语言，使用类 C 语法，内建协程与离散时序模型。它的语法来自 C，类型系统来自 Rust，内存模型来自 C#/Java，协程来自 Go，并发与时序词汇则来自 Verilog/SystemC。

PenguinLang 程序从 `initial` 块启动，而不是 `main` 函数：

```penguin
initial {
	println("hello world from penguin-lang!");
}
```

要运行这个程序需要一个可用的编译工具链——参见[编译器使用](./CompilerUsage.md)。本页余下部分是语言巡览：下面每个示例在 BabyPenguin 参考编译器和 EmperorPenguin 原生编译器上都能编译并运行。

## 变量与类型

变量用 `let` 声明。声明带初始化器时编译器可以推断类型：

```penguin
let x: i32 = 1;      // 显式类型
let y = 2;           // 推断为 i32
let mut z = 3;       // 可变绑定，类型推断
z = 4;               // OK
```

可变性（mutability）是类型的一部分：`let y: mut i32 = 20;` 是不可变绑定后接可变值，`let w: !mut i32 = 30;` 则显式不可变。每个类型要么是**值类型**（赋值即复制——所有基元类型、枚举，以及全部字段都是值类型的类），要么是**引用类型**（赋值共享，由垃圾回收管理）。细节见[类型与模板](./TypesAndTemplates.md)与[数据类型规范](../specifications/03_DataTypes.md)。

## 函数

函数用 `fun` 声明。最后一条语句是表达式时该表达式即为返回值，否则使用 `return`：

```penguin
fun add(a: i32, b: i32) -> i32 {
	return a + b;
}

fun greet() {
	println("hello");
}
```

函数也是值：`fun<R, P1, ...>` 是一等函数类型（第一个类型参数是返回类型），lambda 写作 `fun(x: i32) -> i32 { return x * 2; }`：

```penguin
fun twice(x: i32) -> i32 { return x * 2; }

initial {
	let f: fun<i32, i32> = twice;
	println(cast<string>(f(21)));    // 42
}
```

## 类

类把字段和方法组织在一起。第一个参数是 `this` 的函数是方法；类里不含 `this` 的函数是静态函数：

```penguin
class Point {
	x: i32;
	y: i32;

	fun new(mut this, x: i32, y: i32) {   // 构造器
		this.x = x;
		this.y = y;
	}

	fun length2(this) -> i32 {            // 实例方法
		return this.x * this.x + this.y * this.y;
	}
}

initial {
	let p: mut Point = new Point(3, 4);
	println(cast<string>(p.length2()));   // 25
}
```

`mut this` 表示方法可以修改接收者；普通 `this` 方法在不可变与可变实例上都能调用。由于 `Point` 的两个字段都是值类型，`Point` 本身也是值类型——赋值即复制。含任何引用类型字段的类则是引用类型。

## 枚举

枚举是 Rust 风格的带标签联合：每个变体可以携带载荷。用 `is` 检查变体，用变体名读取载荷：

```penguin
#template(T: type)
enum Shape {
	circle: T;
	square: T;
}

initial {
	let s: mut Shape<i32> = new Shape<i32>.square(9);
	if (s is Shape<i32>.square) {
		println("square " + cast<string>(s.square));   // square 9
	}
}
```

标准库的 `Option<T>`（`some`/`none`）和 `Result<T, E>`（`ok`/`error`）就是这样定义的泛型枚举——PenguinLang 没有 `null`。

## 接口

接口是带可选默认实现的方法契约，类似 Rust 的 trait：

```penguin
interface IHello {
	fun name(this: IHello) -> string {
		return "hello";
	}
}

class Foo {
	impl IHello;                      // 使用默认实现
}

class Bar {
	impl IHello {
		fun name(this: IHello) -> string {
			return "bar";
		}
	}
}
```

实现也可以放在类外（`impl IHello for Bar { ... }`），包括为泛型特化实现。接口是调度机制：静态类型为接口的值在运行时经虚表调用。

## 泛型（#template）

泛型类型与泛型函数用 `#template` 声明。泛型做单态化——每个实例化在编译期单独特化，类似 C++ 模板：

```penguin
#template(T: type)
class Box {
	value: T;

	fun new(mut this, v: T) {
		this.value = v;
	}
}

initial {
	let b: mut Box<i32> = new Box<i32>(5);
	println(cast<string>(b.value));   // 5
}
```

模板参数也可以是值（`#template(N: i32)`），定长类型如 `std.Array<T, N>` 就是这样表达的。参见[类型与模板](./TypesAndTemplates.md)。

## 控制流

`if` 与 `while` 既是语句也是表达式（块的值是其最后一个表达式）；`for` 只作用于可迭代对象的 for-in 形式：

```penguin
initial {
	let y: i32 = if (true) { 2 } else { 3 };

	for (let i: i64 in range(0, 3)) {
		print(cast<string>(i));       // 012
	}
	println("");

	// try-bind：仅当 Option 持有载荷时执行循环体
	let a = new Option<i32>.some(42);
	if (let v := a.some) {
		println(cast<string>(v));     // 42
	}
}
```

错误以值表达（`Result<T, E>`、`Option<T>`），运行时故障则用 `panic`/`try`/`catch`：

```penguin
initial {
	try {
		panic("boom");
	} catch (e) {
		println(e.message);           // boom
	}
}
```

## 字符串

`string` 是不可变引用类型，带完整的方法面（`length`、`substring`、`find`、`replace`、`split` 等）。拼接用 `+`，`cast<string>(x)` 把任意基元转换为字符串：

```penguin
initial {
	let s: string = "Hello, Penguin!";
	println(s.to_upper());                  // HELLO, PENGUIN!
	println(s.substring(7, 7));             // Penguin!

	for (let part: string in "one,two,three".split(",")) {
		print(part + "|");                  // one|two|three|
	}
	println("");
}
```

## 生成器

使用 `yield` 的函数是生成器：返回一个 `IGenerator<T>`，每次 `yield` 产出一个值，可被 for-in 消费：

```penguin
fun count_up() -> IGenerator<i32> {
	yield 1;
	yield 2;
	yield 3;
}

initial {
	for (let v: i32 in count_up()) {
		print(cast<string>(v));       // 123
	}
	println("");
}
```

生成器需要协程支持（EmperorPenguin 上为 `--enable-coroutine`）。

## 协程与时间

`async expr` 把一个函数作为并发作业启动并返回 `IFuture<T>`；`wait` 挂起当前例程，直到 future 完成、条件成立、事件发生或时长耗尽：

```penguin
fun work() -> i32 {
	wait 2 tick;
	return 42;
}

initial {
	let task: mut IFuture<i32> = async work();
	println("before wait");
	let a: i32 = wait task;
	println("wait done " + cast<string>(a));
}

initial {
	wait 1 tick;
	println("tick 1");
}
```

它总是按顺序输出：

```
before wait
tick 1
wait done 42
```

因为 `wait 2 tick` 把 `work` 挂起在仿真时钟上，另一个例程推进到 tick 1，随后时钟推进到 tick 2、`work` 返回。PenguinLang 的调度器是协作式单线程的；所有例程共享同一个以 tick 计的离散时钟。这是编写仿真与 RTL 风格模块系统的基础——参见[异步、时序与模块](./AsyncTimingAndModularity.md)。

## 接下来去哪

* [编译器使用](./CompilerUsage.md)——构建编译器并运行第一个原生程序。
* [类型与模板](./TypesAndTemplates.md)——类型系统深入。
* [元编程](./MetaProgramming.md)——编译期函数、反射与代码生成。
* [异步、时序与模块](./AsyncTimingAndModularity.md)——协程、时序模型与模块化设计。
* 各语言特性的精确定义见[规范](../specifications/01_Overview.md)。
