## Interface
Penguin-lang supports interface, which is similar to rust traits. 

```
interface IBook {
    fun get_title() -> string;

    fun get_language(val this: IBook) -> string {
        return "English";
    }
}
```
The above interfaces defines an unimplemented function `get_title`,  and `get_language` with a default implementation. 

To implement the interface, we can use the `impl` keyword:
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

We can also implement the interface outside the class:
```
class BookB {
    val language: string = "English";
}

impl IBook for BookB {
    fun get_title() -> string {
        return "B";
    }

    fun get_language(val this: IBook) -> string {
        val self: BookB = this as BookB;
        return self.language;
    }
}
```

Note that functions that have `this` as parameter in interface have to use the interface type, and be casted to the actual type in the implementation.

It's possible to use `where` keyword to specify constraints on the type parameters of the interface.
```
class IntegerBook<T> {
    impl IBook where T: Integer {
        fun get_title() -> string {
            return "Integer Book";
        }
    }
}
```