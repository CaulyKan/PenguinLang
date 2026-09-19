# Class

本章定义类：字段、构造器、方法、静态函数与泛型类。类是数据抽象的单位；带端口时它同时也是模块（见[模块化编程](./10_ModularProgramming.md)）。

## 声明

```penguin
class Point {
	x: i32;
	y: i32;
}
```

类由字段声明、函数（方法）、`impl` 块（接口实现，见[接口](./07_Interface.md)）、端口声明（见[模块化编程](./10_ModularProgramming.md)）以及嵌套 `initial`/`construct` 块组成。不带显式 `impl`/端口形式的成员就是字段与函数。

## 字段

字段声明为 `name : [mut|!mut] type [= initializer]`。无显式标记的字段可变性跟随所属对象：

```penguin
class MyClass {
	a: i32 = 1;        // 跟随对象：仅 mut 对象可写
	b: mut i32 = 1;    // 显式可变：即使对象不可变也可写
	c: !mut i32 = 1;   // 显式不可变：构造后冻结
}

initial {
	let obj : MyClass = new MyClass();
	// obj.a = 2;      // 错误：obj 不可变所以 a 不可变
	obj.b = 2;         // OK
	// obj.c = 2;      // 错误：c 显式不可变

	let obj2 : mut MyClass = new MyClass();
	obj2.a = 2;        // OK：对象可变，a 跟随
}
```

不可变成员（`!mut`，或不可变对象的任何成员）只能在构造器或字段自身初始化器中赋值。

## 构造器

构造器是名为 `new` 的函数。首参必须是 `mut this`；参数跟在其后。一个类至多一个构造器——重载 `new` 是编译错误（`E_DUPLICATE_SYMBOL`）：

```penguin
class Point {
	x: i32;
	y: i32;

	fun new(mut this, x: i32, y: i32) {
		this.x = x;
		this.y = y;
	}
}

initial {
	let foo : mut Point = new Point(1, 2);
}
```

类未定义 `fun new` 时，编译器生成**无参默认构造器**——每个字段取其初始化器值或类型零值。带参数调用默认构造器是编译错误：

```penguin
class Unit { count: i32 = 5; }

initial {
	let u : mut Unit = new Unit();        // OK，count = 5
	// let v = new Unit(3);               // 错误：默认构造器不接收参数
}
```

## 方法与静态函数

类内函数首参为 `this` 时是**方法**，否则是**静态函数**：

```penguin
class Foo {
	name: string = "Foo";

	fun hello_world() {            // 静态函数：无 this
		println("hello");
	}

	fun hello_myself(this) {       // 实例方法：只读接收者
		println("hello " + this.name);
	}

	fun rename(mut this, n: string) {   // 实例方法：可变接收者
		this.name = n;
	}
}

initial {
	let mut foo = new Foo();
	foo.hello_myself();     // OK，'foo' 实例作为 'this' 传入
	foo.rename("Bar");      // OK，mut 接收者
	foo.hello_world();      // OK，静态函数可经实例调用
	Foo.hello_world();      // OK，也可直接以类调用
}
```

* `this`——方法读取接收者；不可变与可变实例上均可调用。
* `mut this`——方法可写接收者；仅可变实例上可调用。
* 无接收者的方法是静态的；不能访问实例字段，以 `Class.name(...)` 调用（或经实例）。

方法内 `this.field` 访问字段；裸 `field` 也能解析到成员。`Self` 指代所属类的类型：

```penguin
class Counter {
	value: i32 = 0;
	fun bumped(this) -> Self {
		let c : mut Counter = new Counter();
		return c;
	}
}
```

## 值类与引用类

每个类被分类为值类型或引用类型（完整规则见[数据类型](./03_DataTypes.md)）：字段全为值 → 值类型（自动 `IValueType`，赋值复制）；含引用字段 → 引用类型（自动 `IReferenceType`，赋值共享）。`impl IValueType;` / `impl IReferenceType;` 可覆盖自动分类。值类在未自定义时自动生成 `ICopy<Self>` 逐成员复制。

## 泛型类

`#template` 在类上声明泛型参数；每个实例化被单态化（每个实参集合单独编译）：

```penguin
#template(T: type)
class Box {
	value: T;

	fun new(mut this, v: T) {
		this.value = v;
	}
}

initial {
	let b : mut Box<i32> = new Box<i32>(1);   // Box<i32> 是独立的特化类型
}
```

特化类型的全名携带实参表（`Box<i32>`）。模板参数是类型参数（`T: type`）或编译期值参数（`N: i32`——原生编译器，见[元编程](./11_MetaProgramming.md)）；可变性经类型参数流动（`Box<mut i32>` 存可变值）。

方法可声明自己的模板参数，按调用点类型实参特化（由 EmperorPenguin 编译器实现——Pass1 到 Pass3；BabyPenguin 参考编译器不支持方法级模板）：

```penguin
class Picker {
	#template(U: type)
	fun pick(mut this, x: U) -> U { return x; }
}

initial {
	let p : mut Picker = new Picker();
	println(p.pick<string>("hey"));    // hey
}
```

泛型类体在每个实例化上各绑定一次——体内模板代码可使用能看到具体实参的 `#if`/`#fun` 元编程。

## 类内的枚举、接口、impl

* 类内 `impl IX { ... }` 块为该类实现接口（见[接口](./07_Interface.md)）。
* 嵌套 `initial { ... }` 块使类的实例成为并发例程（每个实例在构造时启动其 initial——模块模式）。
* 嵌套 `construct { ... }` 块在 `new` 时、构造器之后、实例 initial 启动之前运行——`connect` 唯一合法的位置。
