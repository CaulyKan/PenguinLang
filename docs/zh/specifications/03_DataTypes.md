# 数据类型

PenguinLang 是静态类型的。当编译器能从初始化器或上下文推断时，可省略显式类型标注。

本章定义基元类型、值/引用分类、可变性与赋值语义。类、枚举、接口有各自章节；泛型用 `#template` 声明（见 [Class](./05_Class.md) 与[元编程](./11_MetaProgramming.md)）。

## 基元类型

| 类型名 | 大小 |
| --------- | ---- |
| i8        | 1    |
| i16       | 2    |
| i32       | 4    |
| i64       | 8    |
| u8        | 1    |
| u16       | 2    |
| u32       | 4    |
| u64       | 8    |
| bool      | 1    |
| char      | 4    |
| float (f32) | 4  |
| double (f64) | 8 |
| string    | 引用 |
| void      | 0    |

`float`/`double` 是关键字拼写；`f32`/`f64` 是可用别名。

`string` 是**不可变引用类型**：字符串值是指向带头部块（`metaptr + length + data`，见 *EmperorPenguin LLVM*）的指针，创建后内容不可修改（每个产生字符串的内建操作都分配新串），赋值/传参**共享指针**（正因为内容不可变，共享是安全的）。用 `==` 比较（内容相等）；用 `StringBuilder` 构建。

### `IStringOps`——字符串方法

基元 `string` 实现了 `__builtin.IStringOps` 接口（`impl IStringOps for string`，位于 `Builtin.penguin` / `core_builtin.penguin`），因此每个常见字符串操作都是任意字符串上的方法调用：

```penguin
let s: string = "Hello, Penguin!";
println(cast<string>(s.length()));        // 15
println(s.substring(7, 7));                // "Penguin"
println(s.to_upper());                     // "HELLO, PENGUIN!"
println("  pad ".trim());                  // "pad"
println("a-b-c".replace("-", "+"));        // "a+b+c"
for (let part : string in "one,two".split(",")) { ... }
```

| 分组 | 方法 |
| ----- | ------- |
| 查询 | `length() -> i64`、`is_empty() -> bool`、`char_at(index) -> string`（越界 → `""`）、`char_code() -> i64`（首个单元，空串 −1）、`char_code_at(index) -> i64`（越界 −1） |
| 切片 | `substring(start, length)`（钳制）、`slice(start, length)`（不钳制快速路径） |
| 搜索 | `find(sub)`、`find_from(sub, start)`、`find_last(sub)`（均 `-> i64`，−1 = 未找到；`find_last` 的空 `sub` → −1）、`contains(sub)`、`starts_with(prefix)`、`ends_with(suffix)`、`count(sub)`（不重叠；空 → 0） |
| 比较 | `equals_ignore_case(other)`、`compare(other)`（字典序，负/零/正） |
| 变换 | `to_upper()`、`to_lower()`（ASCII）、`trim()`、`trim_start()`、`trim_end()`（空白 = 空格/制表/CR/LF）、`replace(from, to)`（空 `from` → 不变）、`reverse()`、`repeat(n)`（n ≤ 0 → `""`）、`pad_left(width, ch)`、`pad_right(width, ch)`（取 `ch` 首单元，空时用空格；已 ≥ width 时无操作） |
| 分割 | `split(sep) -> mut IIterator<string>`——惰性、可 for-in；按不重叠分隔符切分，尾随分隔符产出最后一个空片段（类 Python），空 `sep` 整串产出一次 |
| 转换 | `to_int() -> i64`（失败 0）、`to_double() -> double` |

所有操作按索引/单元计（原生运行时按字节单元，BabyPenguin VM 按 UTF-16 单元——ASCII 下一致），大小写/修剪表是 ASCII。方法调用直接分派——不要对 string 使用 `cast<IStringOps>`/`is IStringOps`（不支持基元的接口类型装箱）。

## 引用类型与值类型

Penguin-lang 同时支持引用类型与值类型。引用类型持有对象的引用，可作为引用传给其他函数。值类型持有自身数据，赋值给其他变量时被复制。

| 类型            | 谁                                                      | 管理者                     | 赋值       |
| --------------- | -------------------------------------------------------- | ------------------------------ | ---------------- |
| 值类型     | i32、f64、bool……<br /> 实现 `IValueType` 的类 | 栈或父级数据结构 | 总是复制    |
| 引用类型 | 其他任意类型（实现 `IReferenceType`），含 `string` | GC                             | 共享引用 |

> `string` 是引用类型（赋值共享指针），但**内容不可变**——见上文说明；它不出现在值类型一行。

### `IValueType` 与 `IReferenceType`

类的值/引用身份由是否实现 `IValueType` 或 `IReferenceType` 决定。二者是标记接口——没有方法。

```penguin
class ValueClass {
	x: i32;
	y: i32;
	impl IValueType;  // 显式标记为值类型
}

class RefClass {
	data: string;
	impl IReferenceType;  // 显式标记为引用类型
}
```

### 自动分类

若类**没有**显式实现 `IValueType` 或 `IReferenceType`，编译器自动判定：

- 若类的**所有**字段都是值类型（基元、枚举、实现 `IValueType` 的类），自动实现 `IValueType`
- 否则自动实现 `IReferenceType`

```penguin
class Point {
	x: i32;
	y: i32;
	// 字段全为值类型 → 自动 IValueType（值类型）
}

class Node {
	data: i32;
	next: Node;  // 引用类型字段 → 自动 IReferenceType（引用类型）
}
```

### `ICopy<T>`——复制机制

`ICopy<T>` 定义类**如何**复制。它与值/引用分类**相互独立**——值类型和引用类型都可以实现 `ICopy<T>`。

```penguin
#template(T: type)
interface ICopy {
	extern fun copy(this: T) -> T;
}
```

未手动实现 `ICopy<T>` 的**值类型**，编译器自动生成逐成员复制（memcpy 风格结构体复制）的 `ICopy<T>` 实现。

**引用类型**不自动生成 `ICopy<T>`。实现 `ICopy<T>` 的引用类型必须自带深复制逻辑。

### 自动生成规则汇总

| 类显式实现 | 字段全为值类型 | 编译器追加 |
|---|---|---|
| （无） | 是 | `IValueType` + `ICopy<Self>` |
| （无） | 否 | `IReferenceType` |
| `IValueType` | — | `ICopy<Self>`（若未手动提供） |
| `IValueType` + 手动 `ICopy<Self>` | — | （无） |
| `IReferenceType` | — | （无） |

### 值复制语义实践

值类型（基元、枚举、`IValueType` 类）在**每个值模型边界复制**；`mut` 只是**编译期许可**，绝不改变值的存储、布局或共享方式：

*   **绑定与赋值复制**：`let b = a;`（或 `let mut b = a;`）给 `b` 自己独立的值副本。改 `b` 永远不会经 `a` 可见——无论任一侧有无 `mut`。
    ```penguin
    let mut a = new Point(1, 2);
    let mut b = a;   // b 是 a 的副本
    b.x = 9;         // 写 b 自己的存储
    print(cast<string>(a.x)); // 1——a 未变
    ```
*   **参数复制，接收者别名**：普通 `mut` 参数（`fun f(p : mut Point)`）收到**副本**——参数上的 `mut` 只允许改局部副本。例外是方法接收者：`mut this` 方法（`fun set(mut this, ...)`）在调用者实际槽位上调用，其写入会生效（`e.a.increment()` 改变 `e` 的载荷）。
*   **提取复制，链写入寻址槽位**：提取内联成员（`let q = w.p;`、`let q = o.some;`）产生副本。经链写入（`w.p.x = 42;`、`o.some.x = 9;`、`o.some.increment();`）是*左值寻址*——不经中间副本直接写入 `w`/`o` 内部的槽位，写入生效。
*   **非左值链写入被拒绝**：若写链的基是临时对象（`makeWrap().p.x = 9;`、`list.at(0).x = 9;`、`cast<IFoo>(x).v = 9;`），编译器报 `error[E_MUTABILITY]`——写入会落进被丢弃的副本。
*   **容器元素复制**：`List<T>` 的元素访问（`at()`、for 循环变量）复制值类型元素（值类型下 `List<mut T>` 与 `List<T>` 元素布局相同）。要让修改生效，用 `set()` 显式写回。
    ```penguin
    let x : mut Foo = a.at(i).some;
    x.setVal(x.getVal() + 10);
    a.set(i, x);     // 不写回，修改留在副本里
    ```
*   **值类型转接口装箱（复制）**：`let i : IMyInterface = cast<IMyInterface>(p);` 把 `p` 复制进新的箱；之后对 `p` 的修改经 `i` 不可见。绑定与调用参数处的隐式值→接口转换同理。
*   **递归值布局是错误**：内联字段图包含自身的值类/枚举没有有限布局；编译器报 `error[E_SIZE_CYCLE]`。刻意的间接请用引用类型或 `Box<T>`。

## 可变性

Penguin-lang 有一套编译期强制、显式而细粒度的可变性系统，目标是防止意外修改、促成更可预测的代码。

### 变量声明与可变性关键字
变量用 `let` 声明，共四种形式：

*   **`let x : T = v`**：不可变绑定，不可变值。
    ```penguin
    let x : i32 = 10; // x 不可变
    x = 20;          // 编译错误：不能重赋值不可变变量
    ```
*   **`let x : mut T = v`**：不可变绑定，可变值——`mut` 在**类型**上。
    ```penguin
    let y : mut i32 = 20; // y 可变
    y = 30;              // OK：可以重赋值可变变量
    ```
*   **`let mut x = v`**：可变绑定，类型推断——`mut` 在 `let` 上，且**不允许**类型标注。
    ```penguin
    let mut z = 30; // z 可变，推断为 i32
    z = 40;        // OK
    let mut z : i32 = 30; // 编译错误：'let mut' 不能与显式类型标注同用
    ```
*   **`let x : !mut T = v`**：显式标记值不可变。
    ```penguin
    let w : !mut i32 = 30; // w 显式不可变
    w = 40;               // 编译错误
    ```

### 类成员可变性
类成员也可用 `mut` 或 `!mut` 声明。

*   **默认（隐式不可变）**：成员未写 `mut`/`!mut` 时，可变性跟随所属对象。
    ```penguin
    class MyClass {
        a : i32 = 1; // 'a' 隐式不可变
    }
    let obj : MyClass = new MyClass();
    obj.a = 2; // 编译错误：不能赋值不可变成员
    let obj2 : mut MyClass = new MyClass();
    obj2.a = 2; // OK
    ```
*   **显式可变成员**：
    ```penguin
    class MyClass {
        b : mut i32 = 1; // 'b' 显式可变
    }
    let obj : MyClass = new MyClass();
    obj.b = 2; // OK：'b' 可变，即使 'obj' 不可变
    ```
*   **显式不可变成员**：
    ```penguin
    class MyClass {
        c : !mut i32 = 1; // 'c' 显式不可变
    }
    let obj : mut MyClass = new MyClass();
    obj.c = 2; // 编译错误：不能赋值显式不可变成员
    ```

### 可变性与泛型
可变性可用于泛型类型参数与成员。

*   **泛型成员可变性**：
    ```penguin
    #template(T: type)
    class Box {
        value : T; // 'value' 的可变性继承自 'T'
    }
    initial {
        let b : Box<mut i32> = new Box<mut i32>(1); // 'b' 内的 'value' 可变
        b.value = 2; // OK
    }
    ```
*   **泛型成员的显式可变性**：
    ```penguin
    #template(T: type)
    class Container {
        data : mut T; // 'data' 永远可变，无论 'T'
        data2 : !mut T; // 'data2' 永远不可变，无论 'T'
    }
    initial {
        let c : Container<i32> = new Container<i32>(1);
        c.data = 2; // OK
    }
    ```

### 赋值兼容性
不同可变性变量之间的赋值有严格规则。

*   **值类型**：赋值总是复制，可变性不影响赋值兼容。
    ```penguin
    let a : i32 = 1;
    let b : mut i32;
    b = a; // OK：'a' 的值复制给 'b'
    ```
*   **引用类型**：
    *   **可变到不可变（后续赋值）**：不允许。不可变变量在初始声明后不能再被赋可变引用。
        ```penguin
        let a : mut MyClass = new MyClass();
        let b : MyClass;
        b = a; // 编译错误：不能重赋值不可变变量 'b'
        ```
    *   **不可变到可变**：可变变量不能被赋不可变引用。这防止把不可变引用“升级”为可变引用后被用来修改本应不可变的对象。
        ```penguin
        let a : MyClass = new MyClass();
        let b : mut MyClass;
        b = a; // 编译错误
        ```

### 函数调用可变性
函数参数可指明期望的可变性。

*   **参数可变性**：
    ```penguin
    fun foo(a : MyClass, b : mut MyClass) {
        // 'a' 在 foo 内不可变，'b' 可变
    }
    initial {
        let x : MyClass = new MyClass();
        let y : mut MyClass = new MyClass();
        foo(x, y); // OK
        foo(y, x); // 编译错误：不能把不可变 'x' 传给可变参数 'b'
    }
    ```
*   **方法中的 `this` 可变性**：方法可指明其调用实例的可变性。
    *   `fun myMethod(this)`：可在不可变或可变实例上调用，不能修改实例。
    *   `fun myMutableMethod(mut this)`：只能在可变实例上调用，允许修改实例。
    注意与普通参数的不对称：`mut` **参数**收到调用者值的副本（被调方内的修改不会外泄），而 `mut this` **接收者**在调用者实际对象上调用——其写入对调用者可见。
    ```penguin
    class Example {
        value : i32 = 0;
        fun get_value(this) {
            print(cast<string>(this.value));
        }
        fun set_value(mut this, new_value : i32) {
            this.value = new_value;
        }
    }
    initial {
        let immutable_ex : Example = new Example();
        immutable_ex.get_value(); // OK
        immutable_ex.set_value(1); // 编译错误：不能在不可变实例上调用可变方法

        let mutable_ex : mut Example = new Example();
        mutable_ex.get_value(); // OK
        mutable_ex.set_value(1); // OK
    }
    ```

## 内建数据结构

Penguin-lang 提供若干内建数据结构。

*   **`Option<T>`**：可选值。可以是 `some(T)` 或 `none`。用于替代 `null` 安全表达值缺失。
    ```penguin
    #template(T: type)
    enum Option {
        some: T;
        none;
    }
    ```
*   **`Result<T, E>`**：用于返回与传播错误。可以是 `ok(T)` 或 `error(E)`。
*   **`List<T>`**：可增长的堆上列表。元素访问（`at()`、for 循环变量）**复制**值类型元素——用 `set()` 写回修改（见*值复制语义实践*）。
*   **`Queue<T>`**：队列。

## `Self` 类型
`Self` 关键字可在类或接口内指代当前类或接口的类型。

```penguin
interface IFoo {
    fun a() -> Self;
}

class Foo {
    fun a() -> Self {
        return new Foo();
    }
}
```

## 类型别名
`type` 关键字为已有类型创建新名字。

```penguin
type MyInt = i32;

let x : MyInt = 10;
```

## 类型检查与转换
Penguin-lang 用 `cast` 函数做类型转换，`is` 关键字做类型检查。

```
let a : i32 = 1;
let b : f32 = cast<f32>(a);

if (a is i32) {
    // ...
}
```

隐式类型转换规则：
 * 安全的基元类型转换，包括 i32 到 i64、f32 到 f64 等。
 * 基元类型到 string 的转换。
 * 对象到其实现接口的转换。

```
let a : i32 = 1;
let b : i64 = 2;

b = a; // OK
a = b; // 编译错误
a = cast<i32>(b); // OK，但可能损失数据
```

类/接口类型之间的转换：对象 → 已实现接口是隐式的；接口 → 具体类需要 `cast`（接口下转失败抛运行时错误）；值类型 → 接口装箱（复制），接口 → 值类型拆箱。
