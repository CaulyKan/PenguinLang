# 接口

Penguin-lang 支持接口，类似 Rust 的 trait。

```
interface IBook {
    fun get_title() -> string;  // 静态函数

    fun get_language(this: IBook) -> string {  // 成员函数
        return "English";
    }
}
```
上面的接口定义了未实现的函数 `get_title`，以及带默认实现的 `get_language`。

用 `impl` 关键字实现接口：
```
class BookA {
    impl IBook {
        fun get_title() -> string {
            return "A";
        }

        // 使用 get_language 的默认实现
    }
}
```

也可以在类外实现接口：
```
class BookB {
    language: string = "English";
}

impl IBook for BookB {
    fun get_title() -> string {
        return "B";
    }

    fun get_language(this: IBook) -> string {
        let self = cast<BookB>(this);
        return self.language;
    }
}
```

注意接口中以 `this` 为参数的函数必须使用接口类型。实现时可能需要转换到实际类型。

泛型接口带类型参数实现：
```
#template(T: type)
interface IFoo {
    fun foo(this: IFoo<T>) -> T;
}

#template(T: type)
class MyClass {
    impl IFoo<i32> {
        fun foo(this: IFoo<i32>) -> i32 {
            return 1;
        }
    }
}
```

## 调度

静态类型为接口的值经**虚表**分派方法调用：每个实现类型携带把接口方法映射到实现的虚表，编译期构建。两种转换连接对象与接口：

* **装箱**——值类型转换为接口会把它复制进堆上的箱（`cast<IFoo>(valueType)` 及绑定/调用参数处的隐式转换）。
* **拆箱**——`cast<Concrete>(iface)` 取回值；下转失败抛运行时错误。

引用类型不被复制：接口引用与类引用是同一个指针。

## 标记接口

类型系统中的若干接口没有方法，作为标记使用（见[数据类型](./03_DataTypes.md)）：

* `IValueType` / `IReferenceType`——把类分类为值类型或引用类型（否则自动判定）。
* `ICopy<T>`——带 `extern fun copy(this: T) -> T` 的可选复制语义。
* `IHash`、`IUniqueMangleName`、`ISynchronizable`——库契约（哈希、值模板实参的唯一命名、线程安全的引用共享）。

## 孤儿规则与默认方法

`impl X for Y` 块必须与 `X` 或 `Y` 之一同文件（孤儿规则，编译期检查）。`impl` 块中省略的方法回退到接口的默认实现。当前没有从覆写方法内部调用默认实现的语法——直接引用接口方法（`IBook.get_language(this)`）会被拒绝，因为接口方法没有单一实现；请通过具体类引用。

接口可对基元类型实现（`impl IStringOps for string`），也可对泛型特化实现（`impl IBook for Shelf<i32>`——每个实例化有自己的虚表）：

```penguin
#template(T: type)
class Shelf {
    item: T;
}

impl IBook for Shelf<i32> {
    fun get_title() -> string {
        return "int shelf";
    }
}
```

实现也可以由 `#specializing` 块按实例化注入（见[元编程](./11_MetaProgramming.md)）。把特化泛型值类型装箱为接口值当前在 EmperorPenguin 上产出无效代码——由 `Tests/InterfaceTest/GenericValueClassBoxing.md` 跟踪。
