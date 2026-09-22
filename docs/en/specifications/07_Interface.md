# Interface

Penguin-lang supports interfaces, which are similar to Rust traits.

```
interface IBook {
    fun get_title() -> string;  // static function

    fun get_language(this: IBook) -> string {  // member function
        return "English";
    }
}
```
The above interface defines an unimplemented function `get_title`, and `get_language` with a default implementation.

To implement interface, use `impl` keyword:
```
class BookA {
    impl IBook {
        fun get_title() -> string {
            return "A";
        }

        // use default implementation of get_language
    }
}
```

You can also implement interface outside of class:
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

Note that functions that have `this` as a parameter in the interface must use the interface type. When implementing, you may need to cast to the actual type.

Generic interfaces are implemented with type parameters:
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

## Dispatch

A value whose static type is an interface dispatches method calls through a **vtable**: each implementing type carries a vtable mapping interface methods to implementations, built at compile time. Two forms of conversion connect objects and interfaces:

* **Boxing** — converting a value type to an interface copies it into a heap box (`cast<IFoo>(valueType)` and implicit conversions at bindings/call arguments).
* **Unboxing** — `cast<Concrete>(iface)` retrieves the value back; a failed downcast raises a runtime error.

Reference types are not copied: an interface reference and a class reference are the same pointer.

## Marker Interfaces

Several interfaces in the type system have no methods and act as markers (see [Data Types](./03_DataTypes.md)):

* `IValueType` / `IReferenceType` — classify a class as a value type or reference type (auto-applied otherwise).
* `ICopy<T>` — opt-in copy semantics with an `extern fun copy(this: T) -> T`.
* `IHash`, `IUniqueMangleName`, `ISynchronizable` — library contracts (hashing, unique mangling for value template args, thread-safe reference sharing).

## Orphan Rule and Default Methods

An `impl X for Y` block must be in the same file as either `X` or `Y` (the orphan rule, checked at compile time). A method omitted from an `impl` block falls back to the interface's default implementation. There is currently no syntax to invoke a default implementation from inside an overriding method — referencing the interface method directly (`IBook.get_language(this)`) is rejected because an interface method has no single implementation; reference it through a concrete class instead.

Interfaces can be implemented for primitive types (`impl IStringOps for string`) and for generic instantiations (`impl IBook for Shelf<i32>` — every instantiation gets its own vtable):

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

Implementations can also be injected per instantiation by `#specializing` blocks (see [Meta Programming](./11_MetaProgramming.md)). Boxing follows the same rules for specialized generic value classes: `cast<I>(value)` where the value's type is a generic class instantiation (e.g. `Pair<i32>`) copies the specialized instance into the box, and the box layout is the specialized class's layout.
