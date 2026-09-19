# Function

This chapter defines functions: declarations, parameters, return values, lambdas, function values, stateless vs stateful functions, and generator functions. Methods and constructors inside classes are defined in [Class](./05_Class.md); the suspending semantics of stateful functions are defined in [Async & Timing Model](./09_AsyncAndTimingModel.md).

## Function Declaration

```penguin
fun hello() {
    println("hello");
}

fun add(a: i32, b: i32) -> i32 {
    return a + b;
}
```

The return type follows `->`. A function whose last statement is an expression returns it implicitly:

```penguin
fun double(x: i32) -> i32 {
    x * 2
}
```

## Parameters

Parameters carry types; `mut` on a parameter's type makes the local binding writable:

```penguin
class MyClass {
    foo: i32 = 0;
    impl IReferenceType;   // reference type: assignments share the object
}

fun hello(param1 : i32, param2 : mut i32, param3 : MyClass, param4 : mut MyClass) {
    // param1 = 0;    // ERROR: can't change an immutable param

    param2 = 1;       // can change value of param2,
                      // but will not affect caller because i32 is copied

    // param3 = new MyClass(); // ERROR: can't change an immutable param
    // param3.foo = 1;         // ERROR: can't mutate an immutable param

    param4.foo = 1;           // OK, caller object is mutated (shared reference)
    param4 = new MyClass();   // OK, but only rebinds the local parameter;
                              // the caller's object is NOT changed
}
```

For a value-type parameter, `mut` permits mutating the local copy only — writes never escape the callee. Only the method receiver (`mut this`) aliases the caller's slot (see [Data Types](./03_DataTypes.md)).

## Return Values

Return values also have mutability. By default a return value is immutable; `mut` on the return type makes it mutable (required when the result is assigned to a `mut` field):

```penguin
fun clone(foo: Foo) -> mut Foo {
    return new Foo(foo.x, foo.y);
}

fun borrow(foo: Foo) -> Foo {
    return foo;
}
```

## Stateless and Stateful Functions

Every function is either **stateless** or **stateful**:

* A function that uses `wait` or `yield`, or calls a stateful function, is a **stateful function** — it can suspend and must be resumed by the scheduler.
* All other functions are **stateless** — plain computations that run to completion.

The compiler identifies statefulness automatically (`async` inference); the `async` / `!async` specifiers force or suppress it. Calling a stateful function directly is an implicit `wait` (see [Async & Timing Model](./09_AsyncAndTimingModel.md)). Generator functions (below) are stateful. This is why `fun`/`async_fun` are distinct function types that interconvert.

## Generator Functions

A generator function uses `yield` to return a value and pause execution. Its return type is `IGenerator<T>`:

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

A generator function can also have a final `return` statement.

```penguin
fun test() -> IGenerator<i64> {
    yield 1;
    yield 2;
    return 3;
}
```

Generators need coroutine support (`--enable-coroutine` on EmperorPenguin; on the native runtime each generator is a coroutine with a synthesized context object).

## Lambda Functions

A lambda is a function written inline where a value is expected:

```penguin
fun foo() {
    let f : fun<i32, i32> = fun(x: i32) -> i32 {
        return x * 2;
    };
    println(cast<string>(f(3)));    // 6
}
```

`fun { ... }` is the no-parameter shorthand; `async_fun(params) -> ret { ... }` is the suspending variant.

## Function Values

`fun<R, P1, P2, ...>` is a first-class function value type. The FIRST type argument is the RETURN type, the rest are the parameter types (`fun<i32, i32>` = takes one `i32`, returns `i32`; `fun<void>` = no params, returns nothing). `async_fun<R, P...>` is the suspending variant; `fun` and `async_fun` with equal signatures are interchangeable (calling either runs it inline on the current coroutine stack — the callee's `wait` suspends the caller). Function types nest inside generics (`Option<fun<void>>`, `List<fun<i32, i32>>`).

Four sources of function values:

1. **Function reference** — `let f : fun<i32, i32> = twice;`
2. **Bound method reference** — `let g : fun<i32, i32> = x.call;` — the receiver is fixed at reference time: `g(2)` ≡ `x.call(2)`
3. **Unbound method reference** — `let h : fun<i32, Temp, i32> = ns.Temp.call;` — the receiver stays the FIRST PARAMETER of the function type: `h(x, 2)` ≡ `x.call(2)`. This completes the value form of the call sugar `x.call(2) ≡ ns.Temp.call(x, 2)`. Interface methods (`I.b`) have no unique implementation and cannot be referenced unbound.
4. **Lambda** — `fun(x : i32) -> i32 { return x + 1; }` (or `async_fun(...)` for the suspending variant); `fun { ... }` is the no-parameter shorthand.

**Lambda captures are by-value snapshots** taken when the lambda expression is evaluated: modifying the captured variable afterwards does not change what the closure sees, and writes inside the lambda body only affect the closure's own copy. Closures escape their defining frame (returning them from the function is fine). Capturing `this` or class fields is not supported yet — copy the needed members into locals first. There is no runtime rebinding: a function value cannot be turned back into (function, receiver) parts, and `==` on function values is an identity comparison — references taken from the same source compare equal; per-evaluation values (fresh closures, bound-method invokers) may not.

## Async Functions (summary)

A stateful function spawned with `async` returns an `IFuture<T>`; `wait task` retrieves its result. The full model — implicit waits, scheduling, events — is defined in [Async & Timing Model](./09_AsyncAndTimingModel.md).

## Iterator Combinators

Every iterator (the concrete `RangeIterator` / `MapIterator` / `FilterIterator` classes, and `std.Vector`'s `_VectorIterator`) carries the combinator methods `map` / `filter` / `reduce` / `all` / `any` / `into`. Chaining works because `range()` and `iter()` return the CONCRETE iterator type — combinators on an `IIterator`-typed value are not dispatchable (the vtable slot set of a generic method is not knowable per call site), so keep the chain on the concrete type:

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

*   `map<U>(f)` / `filter(pred)` are LAZY — the source is only pulled when the resulting iterator's `next()` is called.
*   `map` infers `U` from the lambda's return type; `reduce<R>(init, f)` infers `R` from `init`; no explicit type arguments needed.
*   `into<C>()` materializes the iterator into any container with a default constructor and a `push` method — `C` is an EXPLICIT type argument: `v.iter().map(f).into<std.Vector<i64>>()`.
*   `for (let x : i64 in range(0, 3).map(f))` works too — for-in accepts any iterator-typed operand as-is.

The combinator classes are part of the native standard library (EmperorPenguin Pass2/Pass3).

## Block Expressions

A `{ ... }` block is an expression whose value is its final expression; `if` and `while` are expressions too:

```penguin
fun foo() -> i32 {
    let x = { 1 };
    let y = if (x == 1) { 2 } else { 3 };
    let z : i32 = while (true) { break 4; };
    return x + y + z;
}
```

See [Basic Execution Flow](./02_BasicExecutionFlow.md).
