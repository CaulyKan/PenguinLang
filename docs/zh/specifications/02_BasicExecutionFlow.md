# 基本执行流

本章定义 PenguinLang 的控制流：`if`、`while`、`for`、`try`/`catch` 及相关表达式形式。协程（`wait`、`async`）也属于执行流，但定义在[异步与时序模型](./09_AsyncAndTimingModel.md)。

`if`、`while` 与代码块**既是语句也是表达式**——块的值是其最后一个表达式。

## if / else

`if` 既可作语句也可作表达式。表达式位置的值是所执行分支的最后一个表达式：

```penguin
if (x > 0) { print("positive"); }
else if (x == 0) { print("zero"); }
else { print("negative"); }

let y : i32 = if (x == 1) { 2 } else { 3 };
```

条件必须是 `bool`。if 表达式的各分支类型必须兼容。

## while

`while` 也可作表达式；其值来自 `break <expr>;`：

```penguin
while (i < 10) { i += 1; }

let found : i64 = while (true) {
	if (at_end()) { break -1; }
	step();
};
```

接收 while 表达式值的绑定需要显式类型标注（不带标注的 `let mut z = while ...` 会被拒绝）。不带值的 `break` 产生 `void`。从不 break 的 while 表达式求值为 `void`。

## for

`for` 只有 for-in 形式——没有 C 风格的 `for(;;)`。循环变量可带类型标注；`let` 上的 `mut` 选择可变迭代器路径：

```penguin
for (let i : i64 in range(0, 3)) {
	print(cast<string>(i));      // 012
}

for (let item in list) { ... }        // 使用 iter()
for (let mut item in list) { ... }    // 使用 iter_mut()
```

可迭代对象脱糖为基于 `IIterator<T>.next() -> Option<T>` 的 `iter()`/`iter_mut()`；已经是迭代器的表达式按原样使用。`let mut` 不能与显式类型组合——`for (let mut i : i64 in ...)` 是编译错误。

`range(start, end)` 产生标准整数迭代器（不含 end）。迭代器组合子（`map`/`filter`/`reduce`/`all`/`any`/`into`）在原生编译器上可用——见[函数](./04_Function.md)。

## break / continue / return

* `break;` / `break <expr>;` 退出包围循环（while 表达式带值）。
* `continue;` 跳到下一次循环迭代。
* `return;` / `return <expr>;` 退出当前函数。非 void 函数必须在所有路径返回（编译器检查）；函数体以表达式结尾时隐式返回该表达式。
* `initial` 块内，`return` 结束该例程。

循环外的 `break`/`continue` 与 return 值/类型不匹配都是编译错误。

## 块表达式

`{ ... }` 块是表达式，值是其最终表达式：

```penguin
fun foo() -> i32 {
	let x = { 1 };
	let y = if (x == 1) { 2 } else { 3 };
	let z = while (true) { break 4; };
	return x + y + z;             // 1 + 2 + 4
}
```

## try-bind

`if (let x := expr)` 绑定可选值的载荷，仅当其可读时执行循环体：

```penguin
initial {
	let a = new Option<i32>.some(42);
	if (let v := a.some) {
		print(cast<string>(v));   // 仅当 a 持有载荷时运行
	} else {
		print("none");
	}
}
```

允许可选类型标注：`if (let v : i32 := a.some)`。值不可读（`none` 情形）时运行 `else` 分支。

## try / catch

`panic(msg)` 抛出 `RuntimeError`；`try`/`catch` 捕获它：

```penguin
try {
	panic("boom");
} catch (e) {
	print(e.message);             // RuntimeError 有 .message 与 .code
}
```

catch 变量 `e` 是 `__builtin.RuntimeError` 值，含 `message: string` 与 `code: i64` 字段。未捕获的 panic 以非零退出码终止程序。运行时自身抛出的错误（通道关闭等）由同一机制捕获。

## 手写模式匹配

没有 `match`/`case` 语句——把 `is` 检查（枚举变体、类型，见[枚举](./06_Enum.md)）与 `if`/`else` 链组合：

```penguin
if (a is Option<i32>.some) {
	println("some " + cast<string>(a.some));
} else if (a is Option<i32>.none) {
	println("none");
}
```
