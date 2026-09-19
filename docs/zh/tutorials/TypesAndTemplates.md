# 类型与模板

本页巡览 PenguinLang 的类型系统：基元类型、可变性、值/引用二分、类、接口、枚举与泛型。每个概念配一个可运行的例子；精确规则在规范里（[数据类型](../specifications/03_DataTypes.md)、[Class](../specifications/05_Class.md)、[Enum](../specifications/06_Enum.md)、[Interface](../specifications/07_Interface.md)）。

## 静态类型与推断

类型在编译期检查。有初始化器时编译器可推断类型；无初始化器的声明必须写类型标注：

```penguin
let x = 1;            // i32
let y: i64 = 2;       // 标注
let mut z: mut i32 = 3;
```

## 基元类型

| 类型 | 大小（字节） | 说明 |
|---|---|---|
| `i8` `i16` `i32` `i64` | 1/2/4/8 | 有符号整数 |
| `u8` `u16` `u32` `u64` | 1/2/4/8 | 无符号整数 |
| `bool` | 1 | `true` / `false` |
| `char` | 4 | Unicode 码点 |
| `float` / `f32` | 4 | IEEE 754 单精度 |
| `double` / `f64` | 8 | IEEE 754 双精度 |
| `string` | 引用 | 不可变，垃圾回收 |
| `void` | 0 | 无值 |

`float`/`double` 是关键字拼写，`f32`/`f64` 是别名。`string` 不可变：每个产生字符串的内建操作都分配新串，赋值共享指针（正因为内容永不变更，共享是安全的）。

## 可变性

可变性是类型与绑定的编译期属性，不是存储的运行时属性。声明有四种形式：

```penguin
let a: i32 = 1;        // 不可变绑定，不可变值
let b: mut i32 = 1;    // 不可变绑定，可变值——b = 2 合法
let mut c = 1;         // 可变绑定，类型推断——不允许类型标注
let d: !mut i32 = 1;   // 显式不可变值（与第一种形式相同）
```

`mut` 可与泛型组合，成员可以相对对象固定自身的可变性：

```penguin
class Config {
	name: !mut string;   // 构造后冻结，即使是 mut 对象
	retries: mut i32;    // 永远可写，即使对象不可变
	port: i32;           // 跟随对象的可变性
}
```

经链写入（`obj.field.sub = v`）是左值寻址——直接写入 `obj` 内部的槽位。向临时对象写入（`make().field = v`）是编译错误。

## 值类型与引用类型

每个类型非此即彼：

| | 值类型 | 引用类型 |
|---|---|---|
| 成员 | 基元、枚举、字段全为值类型的类（自动 `IValueType`） | 含任何引用类型字段的类（自动 `IReferenceType`）、`string`、接口 |
| 管理 | 栈或父级数据结构 | 垃圾回收器 |
| 赋值 | 总是复制 | 共享引用 |

```penguin
class Point { x: i32; y: i32; }          // 字段全为值 → 值类型

class Node { data: i32; next: Node; }    // 引用字段 → 引用类型
```

值类型赋值即复制；改副本不影响原值：

```penguin
let mut a = new Point(1, 2);
let mut b = a;      // b 是 a 的副本
b.x = 9;
println(cast<string>(a.x));    // 1——a 未变
```

引用赋值则共享：

```penguin
let r1: mut Node = new Node();
let r2: mut Node = r1;    // 同一对象
r2.data = 5;              // 经 r1 可见
```

可以用标记接口 `impl IValueType;` 或 `impl IReferenceType;` 强制分类——两者都是空接口（无方法）。需要刻意间接时用 `Box<T>` 把值类型包成引用类型。引用到值的赋值遵循可变性：可变到不可变隐式允许，不可变到可变被拒绝。

## 类

类承载字段、构造器（`fun new`）、实例方法（首参为 `this`）与静态函数：

```penguin
class Counter {
	count: i32 = 0;

	fun new(mut this, start: i32) {
		this.count = start;
	}

	fun increment(mut this) {      // mut this：可修改接收者
		this.count = this.count + 1;
	}

	fun get(this) -> i32 {         // this：只读接收者
		return this.count;
	}

	fun describe(x: i32) -> string {   // 无 this → 静态函数
		return "counter value " + cast<string>(x);
	}
}

initial {
	let c: mut Counter = new Counter(10);
	c.increment();
	println(cast<string>(c.get()));                      // 11
	println(Counter.describe(c.get()));                  // 以类名做静态调用
}
```

不可变接收者不能调用 `mut this` 方法。未定义 `fun new` 时编译器生成无参默认构造器。细节见 [Class 规范](../specifications/05_Class.md)。

## 枚举

枚举是一组命名变体，每个变体可选携带载荷。用 `new Enum.variant(args)` 构造，用 `is` 检查，用变体名读载荷：

```penguin
enum LogLevel {
	debug;
	info: string;      // 带载荷的变体
}

fun log(level: mut LogLevel) {
	if (level is LogLevel.info) {
		println("INFO " + level.info);
	} else {
		println("DEBUG");
	}
}

initial {
	log(new LogLevel.debug());                    // DEBUG
	log(new LogLevel.info("disk full"));          // INFO disk full
}
```

枚举可以有方法（`fun value_or(this, ...)`）但没有构造器。枚举是值类型。标准库的 `Option<T>` 与 `Result<T, E>` 是泛型枚举。

## 接口

接口定义方法契约，可带默认实现。在类内实现，或在类外用 `impl ... for`：

```penguin
interface IPrintable {
	fun print_label(this: IPrintable) -> string {
		return "?";
	}
}

#template(T: type)
class Pair {
	first: T;
	second: T;
	impl IPrintable {
		fun print_label(this: IPrintable) -> string {
			let self = cast<Pair<T>>(this);       // 下转以访问字段
			return cast<string>(self.first) + "," + cast<string>(self.second);
		}
	}
	fun new(mut this, a: T, b: T) {
		this.first = a;
		this.second = b;
	}
}

initial {
	let p: mut Pair<i32> = new Pair<i32>(3, 4);
	println(p.print_label());                     // 3,4
}
```

类外实现可以面向泛型特化（`impl IPrintable for Pair<i32> { ... }`），也可以挂在基元类型上（`impl IStringOps for string`）。经接口值的调用走虚表。值类型转换为接口会**装箱**（堆上复制）；转回时**拆箱**。

## 模板（泛型）

`#template` 声明泛型参数。实例化做单态化——每个实参集合单独编译：

```penguin
#template(T: type)
class Box {
	value: T;
	fun new(mut this, v: T) { this.value = v; }
	fun get(this) -> T { return this.value; }
}

initial {
	let b: mut Box<string> = new Box<string>("hi");
	println(b.get());                                  // hi
}
```

模板参数分两类：

* **类型参数**（`T: type`）——代表类型；可变性随之流动（`Box<mut i32>` 使存储的值可变）。
* **值参数**（`N: i32`）——编译期常量，由元编程引擎求值，因此只在原生 EmperorPenguin 编译器（Pass2/Pass3）上可用；pass3 专属的定长数组 `std.Array<T, N>` 就建立在其上。形如：

```penguin
#template(N: i32)
enum Buffer {
	slot;
	fun size(this) -> i64 { return N; }
}

initial {
	let b = new Buffer<8>.slot();
	println(cast<string>(b.size()));    // 8
}
```

无法从参数推断时，泛型函数以显式类型实参调用（`filled<i32>()`）。

## 转换与类型检查

`cast<T>(x)` 显式转换；`x is T` 在编译期（类型）或运行时（枚举变体、接口实例）检查：

```penguin
let a: i32 = 1;
let b: f64 = cast<f64>(a);              // 数值转换
let s: string = cast<string>(a);        // 任意基元 → 字符串

let n: i64 = 2;
let c: i32 = cast<i32>(n);              // 收窄需要 cast

let opt = new Option<i32>.some(5);
if (opt is Option<i32>.some) { ... }    // 枚举变体检查
```

隐式转换只允许安全加宽（`i32` → `i64`、`f32` → `f64`）、需要字符串处的基元 → `string`，以及对象 → 已实现接口。

## 接下来去哪

* [数据类型规范](../specifications/03_DataTypes.md)——完整的可变性与赋值兼容规则。
* [元编程](./MetaProgramming.md)——`#template` 之下是编译期函数。
* [异步、时序与模块](./AsyncTimingAndModularity.md)——带端口的类即为并发模块。
