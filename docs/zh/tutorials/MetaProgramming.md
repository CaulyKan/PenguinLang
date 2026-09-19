# 元编程

PenguinLang 的元编程让普通 PenguinLang 代码在**编译期**运行，并把结果拼接回被编译的程序。核心思想：

1. **编译期与运行时同一种语言。** 编译期函数（`#fun`）用普通 `fun` 语法书写——没有独立的宏语言。
2. **顺序代码，而非模式匹配。** 编译期决策是普通函数里的 `if`/`while`，不是 C++ 模板特化规则。
3. **编译器自身的对象就是反射 API。** `#fun` 里的 `type` 参数是真实的 `emperor.BoundType`；`t.fields()` 返回活的字段对象。

元编程由自举的 EmperorPenguin 编译器实现。`#fun` JIT 路径只在**原生编译器**（Pass2、Pass3 及 LSP 内嵌编译器）上运行；硬编码构造（`#define`、`#if`、`#while`）在所有 EmperorPenguin 构建里可用。BabyPenguin（C# 编译器）只解析 `#template`，不执行任何元构造。完整规则见[元编程规范](../specifications/11_MetaProgramming.md)。

## 编译期选项与 #if

`#define("K","V")` 向编译器的选项存储写入键值对；`#defined("K")` 测试存在性，`#option("K")` 读取。命令行 `-DMode=debug` 写入同一个存储。`#if` / `#elif` / `#else` 在编译期在定义之间做选择——每个分支是花括号包裹的定义块，条件由字面量、`#defined`、`#option` 比较与 `!`/`&&`/`||` 折叠而来：

```penguin
#define("Mode", "debug");

#if (#option("Mode") == "debug") {
	fun log_enabled() -> bool { return true; }
} #else {
	fun log_enabled() -> bool { return false; }
}
```

`#while` 展开（unroll）编译期循环（上限 10000 次迭代）。`#for`、`#break`、`#continue` 已可解析；其集合迭代形式尚未实现。

## #fun——编译期函数

`#fun` 声明一个由编译器 JIT 执行（经 LLVM ORC）的函数。调用 `#name(args)` 在编译期求值并原位拼接——零运行时开销：

```penguin
#fun sq(n: i64) -> i64 { return n * n; }

initial {
	let a: i64 = #sq(5);            // 25，编译期算出
	let b: i64 = #sq(6) + #sq(2);   // 40
	println(cast<string>(a) + " " + cast<string>(b));
}
```

规则要点：`#fun` 位于全局/命名空间作用域；体内递归去掉 `#` 前缀；参数与返回类型显式；不允许引用类型返回（编译期数据流承载 `i64`、`bool`、`double`、`string`、`type` 与 AST 令牌）。

### 类型级元函数

返回 `type` 的 `#fun` 计算类型。可在任何需要类型的位置使用——编译器执行它并拼入结果类型：

```penguin
#fun num_kind(t: type) -> type {
	if (t.is_class()) { return #typeof(string); }
	return #typeof(i64);
}

initial {
	let n: #num_kind(i32) = 7;      // 解析为 i64
	println(cast<string>(n));
}
```

`#template` 正是它的语法糖：`#template(T: type) class Box<T>` 脱糖为一个返回特化类的 `#fun Box(T: type) -> type`。值参数（`#template(N: i32)`）走同一机制——模板体是每次实例化求值的元函数。

## 反射

`#fun` 内的 `type` 参数是编译器的活类型对象，带真实方法——没有平行的 `FieldInfo` 体系：

```penguin
class Point { x: i32; y: i32; fun norm(this) -> i32 { return 0; } }

#fun describe(t: type) -> i64 {
	if (t.is_class()) {
		return cast<i64>(t.fields().size());      // 字段数
	}
	return 0;
}

initial {
	println("fields=" + cast<string>(#describe(#typeof(Point))));   // fields=2
}
```

常用成员：`t.fields()` / `t.methods()` / `t.variants()`（带 `.name`、`.bound_type` 等的活定义对象）、`t.is_class()` / `is_enum()` / `is_interface()` / `is_primitive()`、`t.is_value_type()` / `is_reference_type()`、`t.display_name()`。`#typeof(T)` 在元与非元代码中都可用，模板体内解析为具体实例化类型。

## #specializing——条件实现

`#specializing <Type><Args>` 块在每个泛型实例化时运行一次，可以条件注入接口实现。块体是普通编译期代码；其中的 `impl` 附属到特化类型上：

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

initial {
	let a = new foo<5>();
	let da: IDescribe = a;
	println("n5=" + da.describe());    // n5=big
}
```

`foo<5>` 实现了 `IDescribe`；`foo<1>` 没有。这用一个普通 `if` 覆盖了 Rust where 子句 / C++ 偏特化的场景。

## #class 与 #compiler()

* **`#class`** 声明仅元期存在的数据类——为编译期代码记账，绝不进入生成的程序。
* **`#compiler()`** 返回正在编译的编译器的代理。在 `#fun` 体内：`compiler().error("...")` / `.warn(...)` / `.info(...)` 发出诊断，`.set_option` / `.get_option` 管理选项，`.resolve_type("std.Vector")` 按名解析类型，`.create_expression(text)` / `.create_definition(text)` 在元运行时解析源码文本为 AST 对象。

## 代码生成：unstructured_ast

最后一个参数种类为 `ast`（或原始文本的 `unstructured_ast`）的 `#fun` 接受一个**尾随代码块**作为语法。块交付给元函数，可检视、可重发、可返回生成的定义拼接在调用点。注解形式可以标注字段：

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

initial {
	let c: mut C = new C();
	println(c.tag_of_name() + " " + c.name);   // alpha/name field-ok
}
```

标准库的 JSON 序列化（`json.penguin` 的 `#impl_json_serializable()`）就是这样写的：反射字段、拼装 impl 源码文本、注入、照常编译。

## 什么在哪里运行

| 特性 | BabyPenguin | EP Pass1 | EP Pass2/Pass3 / LSP |
|---|---|---|---|
| `#template`（类型参数） | 解析并单态化 | 解析并单态化 | 完整 |
| `#define` / `#defined` / `#option` | — | ✓ | ✓ |
| `#if` / `#while` | — | ✓ | ✓ |
| `#fun` JIT、`-> type`、反射、值模板、`#specializing`、`#class`、`#compiler()`、`-> ast` | — | — | ✓ |

运行元编程测试套件：

```bash
dotnet run --project Tests/PenguinTestRunner -- --filter "MetaProgramming/*" --compilers pass3
```

文法、绑定规则、执行遍与当前未实现清单见[规范](../specifications/11_MetaProgramming.md)。
