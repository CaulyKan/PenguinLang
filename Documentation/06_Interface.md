## Interface
Penguin-lang supports interfaces, which are similar to Rust traits.

```
interface IBook {
    fun get_title() -> string;

    fun get_language(this: IBook) -> string {
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
    const language: string = "English";
}

impl IBook for BookB {
    fun get_title() -> string {
        return "B";
    }

    fun get_language(this: IBook) -> string {
        let self = this as BookB;
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
