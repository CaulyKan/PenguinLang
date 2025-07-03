## Interface
Penguin-lang supports interfaces, which are similar to Rust traits. 

```
interface IBook {
    fun get_title() -> string;

    fun get_language(const this: IBook) -> string {
        return "English";
    }
}
```
The above interface defines an unimplemented function `get_title`, and `get_language` with a default implementation. 

To implement the interface, use the `impl` keyword:
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

You can also implement the interface outside the class:
```
class BookB {
    const language: string = "English";
}

impl IBook for BookB {
    fun get_title() -> string {
        return "B";
    }

    fun get_language(const this: IBook) -> string {
        const self = this as BookB;
        return self.language;
    }
}
```

Note that functions that have `this` as a parameter in the interface must use the interface type, and be cast to the actual type in the implementation.

It is possible to use the `where` keyword to specify constraints on the type parameters of the interface.
```
class IntegerBook<T> {
    impl IBook where T: Integer {
        fun get_title() -> string {
            return "Integer Book";
        }
    }
}
```