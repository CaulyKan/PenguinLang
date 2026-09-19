# 函数

本章定义函数：声明、参数、返回值、lambda、函数值、无状态/有状态函数与生成器函数。类内的方法与构造器定义在 [Class](./05_Class.md)；有状态函数的挂起语义定义在[异步与时序模型](./09_AsyncAndTimingModel.md)。

## 函数声明

```penguin
fun hello() {
	println("hello");
}

fun add(a: i32, b: i32) -> i32 {
	return a + b;
}
```

返回类型跟在 `->` 之后。最后一条语句是表达式的函数隐式返回该表达式：

```penguin
fun double(x: i32) -> i32 {
	x * 2
}
```

## 参数

参数带类型；参数类型上的 `mut` 使局部绑定可写：

```penguin
class MyClass {
	foo: i32 = 0;
	impl IReferenceType;   // 引用类型：赋值共享对象
}

fun hello(param1 : i32, param2 : mut i32, param3 : MyClass, param4 : mut MyClass) {
	// param1 = 0;    // 错误：不能修改不可变参数

	param2 = 1;       // 可以改 param2 的值，
	                  // 但不影响调用者，因为 i32 是复制的

	// param3 = new MyClass(); // 错误：不能修改不可变参数
	// param3.foo = 1;         // 错误：不能修改不可变参数

	param4.foo = 1;           // OK，调用者对象被修改（共享引用）
	param4 = new MyClass();   // OK，但只是重绑定局部参数；
	                          // 调用者的对象不变
}
```

值类型参数上的 `mut` 只允许改局部副本——写入绝不外泄。只有方法接收者（`mut this`）别名调用者的槽位（见[数据类型](./03_DataTypes.md)）。

## 返回值

返回值也有可变性。默认不可变；返回类型上的 `mut` 使其可变（结果赋给 `mut` 字段时需要）：

```penguin
fun clone(foo: Foo) -> mut Foo {
	return new Foo(foo.x, foo.y);
}

fun borrow(foo: Foo) -> Foo {
	return foo;
}
```

## 无状态函数与有状态函数

每个函数要么**无状态**要么**有状态**：

* 使用 `wait` 或 `yield`、或调用有状态函数的函数是**有状态函数**——可以挂起、需由调度器恢复。
* 其余函数是**无状态函数**——运行到完成的普通计算。

编译器自动识别（async 推断）；`async` / `!async` 说明符可强制或抑制。直接调用有状态函数是隐式 `wait`（见[异步与时序模型](./09_AsyncAndTimingModel.md)）。生成器函数（下文）是有状态的。这也是 `fun`/`async_fun` 是两种可互转函数类型的原因。

## 生成器函数

生成器函数用 `yield` 返回一个值并暂停执行。返回类型是 `IGenerator<T>`：

```penguin
fun test() -> IGenerator<i32> {
	yield 1;
	yield 2;
	yield 3;
}

initial {
	for (let v : i32 in test()) {
		println(cast<string>(v));    // 1 2 3
	}
}
```

生成器函数也可以有最终的 `return` 语句。

```penguin
fun test() -> IGenerator<i64> {
	yield 1;
	yield 2;
	return 3;
}
```

生成器需要协程支持（EmperorPenguin 上为 `--enable-coroutine`；原生运行时上每个生成器是一个带合成上下文对象的协程）。

## Lambda 函数

lambda 是在需要值的位置内联书写的函数：

```penguin
fun foo() {
	let f : fun<i32, i32> = fun(x: i32) -> i32 {
		return x * 2;
	};
	println(cast<string>(f(3)));    // 6
}
```

`fun { ... }` 是无参简写；`async_fun(params) -> ret { ... }` 是可挂起变体。

## 函数值

`fun<R, P1, P2, ...>` 是一等函数值类型。**第一个**类型参数是**返回**类型，其余是参数类型（`fun<i32, i32>` = 接收一个 `i32`、返回 `i32`；`fun<void>` = 无参数、无返回）。`async_fun<R, P...>` 是可挂起变体；签名相同的 `fun` 与 `async_fun` 可互换（调用任一都在当前协程栈上内联运行——被调方的 `wait` 挂起调用方）。函数类型可嵌套于泛型（`Option<fun<void>>`、`List<fun<i32, i32>>`）。

函数值的四个来源：

1. **函数引用**——`let f : fun<i32, i32> = twice;`
2. **绑定方法引用**——`let g : fun<i32, i32> = x.call;`——接收者在引用时固定：`g(2)` ≡ `x.call(2)`
3. **未绑定方法引用**——`let h : fun<i32, Temp, i32> = ns.Temp.call;`——接收者保留为函数类型的**第一个参数**：`h(x, 2)` ≡ `x.call(2)`。这补全了调用糖的值形式 `x.call(2) ≡ ns.Temp.call(x, 2)`。接口方法（`I.b`）没有唯一实现，不能未绑定引用。
4. **Lambda**——`fun(x : i32) -> i32 { return x + 1; }`（可挂起变体为 `async_fun(...)`）；`fun { ... }` 是无参简写。

**lambda 捕获是求值时的按值快照**：之后修改被捕获变量不会改变闭包所见，lambda 体内的写入也只影响闭包自己的副本。闭包可以逃离定义帧（从函数返回闭包是合法的）。暂不支持捕获 `this` 或类字段——先把需要的成员复制进局部变量。没有运行时重绑定：函数值不能拆回（函数、接收者）两部分；函数值上的 `==` 是同一性比较——取自同一来源的引用相等；每次求值产生的值（新闭包、绑定方法调用器）可能不等。

## Async 函数（摘要）

用 `async` 启动的有状态函数返回 `IFuture<T>`；`wait task` 取回结果。完整模型——隐式 wait、调度、事件——定义在[异步与时序模型](./09_AsyncAndTimingModel.md)。

## 迭代器组合子

每个迭代器（具体的 `RangeIterator` / `MapIterator` / `FilterIterator` 类，以及 `std.Vector` 的 `_VectorIterator`）都带组合子方法 `map` / `filter` / `reduce` / `all` / `any` / `into`。链式可用是因为 `range()` 与 `iter()` 返回**具体**迭代器类型——`IIterator` 类型值上的组合子不可分派（泛型方法的虚表槽集对每个调用点不可知），因此链要保持具体类型：

```penguin
initial {
	let pull : mut Option<i64> = range(0, 6).map(fun (x: i64) -> i64 { return x * 2; })
		.filter(fun (x: i64) -> bool { return x > 1; }).next();   // some(2)
	let total : i64 = range(0, 4)
		.map(fun (x: i64) -> i64 { return x + 1; })
		.reduce(0, fun (acc: i64, x: i64) -> i64 { return acc + x; }); // 1+2+3+4 = 10
	println(cast<string>(pull.some) + " " + cast<string>(total));
}
```

*   `map<U>(f)` / `filter(pred)` 是**惰性**的——只在结果迭代器的 `next()` 被调用时才拉取源。
*   `map` 从 lambda 返回类型推断 `U`；`reduce<R>(init, f)` 从 `init` 推断 `R`；无需显式类型实参。
*   `into<C>()` 把迭代器物化为任何带默认构造器与 `push` 方法的容器——`C` 是**显式**类型实参：`v.iter().map(f).into<std.Vector<i64>>()`。
*   `for (let x : i64 in range(0, 3).map(f))` 也可——for-in 按原样接受任何迭代器类型的操作数。

组合子类属于原生标准库（EmperorPenguin Pass2/Pass3）。

## 块表达式

`{ ... }` 块是表达式，值是其最终表达式；`if` 与 `while` 也是表达式：

```penguin
fun foo() -> i32 {
	let x = { 1 };
	let y = if (x == 1) { 2 } else { 3 };
	let z : i32 = while (true) { break 4; };
	return x + y + z;
}
```

见[基本执行流](./02_BasicExecutionFlow.md)。
