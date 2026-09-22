# Basic Execution Flow

This chapter defines PenguinLang's control flow: `if`, `while`, `for`, `try`/`catch`, and the related expression forms. Coroutines (`wait`, `async`) are part of the execution flow too but are defined in [Async & Timing Model](./09_AsyncAndTimingModel.md).

`if`, `while`, and code blocks are **expressions as well as statements** — a block's value is its last expression.

## if / else

`if` works as a statement and as an expression. In expression position the value is the last expression of the executed branch:

```penguin
if (x > 0) { print("positive"); }
else if (x == 0) { print("zero"); }
else { print("negative"); }

let y : i32 = if (x == 1) { 2 } else { 3 };
```

The condition must be `bool`. The branches of an if-expression must have compatible types.

## while

`while` also works as an expression; its value comes from `break <expr>;`:

```penguin
while (i < 10) { i += 1; }

let found : i64 = while (true) {
    if (at_end()) { break -1; }
    step();
};
```

The binding that receives a while-expression value needs an explicit type annotation (`let mut z = while ...` without one is rejected). A `break` without a value yields `void`. A while-expression whose loop never breaks evaluates to `void`.

## for

`for` is for-in only — there is no C-style `for(;;)`. The loop variable may carry a type annotation; `mut` on the `let` selects the mutable iterator path:

```penguin
for (let i : i64 in range(0, 3)) {
    print(cast<string>(i));      // 012
}

for (let item in list) { ... }        // uses iter()
for (let mut item in list) { ... }    // uses iter_mut()
```

The iterable desugars to `iter()`/`iter_mut()` over `IIterator<T>.next() -> Option<T>`; an expression that is already an iterator is used as-is. `let mut` cannot be combined with an explicit type — `for (let mut i : i64 in ...)` is a compile error.

`range(start, end)` produces the standard integer iterator (end-exclusive). Iterator combinators (`map`/`filter`/`reduce`/`all`/`any`/`into`) are available on the concrete iterator classes on the native compilers — see [Function](./04_Function.md).

## break / continue / return

* `break;` / `break <expr>;` exits the enclosing loop (with a value for while-expressions).
* `continue;` jumps to the next loop iteration.
* `return;` / `return <expr>;` exits the current function. A non-void function must return on all paths (checked by the compiler); a function whose body ends in an expression returns it implicitly.
* Inside an `initial` block, `return` ends that routine.

`break`/`continue` outside a loop and `return` value/type mismatches are compile errors.

## Block Expressions

A `{ ... }` block is an expression whose value is its final expression:

```penguin
fun foo() -> i32 {
    let x = { 1 };
    let y = if (x == 1) { 2 } else { 3 };
    let z = while (true) { break 4; };
    return x + y + z;             // 1 + 2 + 4
}
```

## try-bind

`if (let x := expr)` binds the payload of an optional value and runs the body only when it is readable:

```penguin
initial {
    let a = new Option<i32>.some(42);
    if (let v := a.some) {
        print(cast<string>(v));   // runs only when a holds a payload
    } else {
        print("none");
    }
}
```

An optional type annotation is allowed: `if (let v : i32 := a.some)`. The `else` branch runs when the value is not readable (the `none` case). The bound expression may read a global `Option` as well as a local — a module-level `let a = new Option<i32>.some(42);` followed by `if (let v := a.some)` prints the payload the same way.

## try / catch

`panic(msg)` throws a `RuntimeError`; `try`/`catch` catches it:

```penguin
try {
    panic("boom");
} catch (e) {
    print(e.message);             // RuntimeError has .message and .code
}
```

The catch variable `e` is a `__builtin.RuntimeError` value with `message: string` and `code: i64` fields. Uncaught panics terminate the program with a nonzero exit code. Runtime errors raised by the runtime itself (channel closed, etc.) are caught by the same mechanism.

## Pattern Matching by Hand

There is no `match`/`case` statement — combine `is` checks (enum variants, types — see [Enum](./06_Enum.md)) with `if`/`else` chains:

```penguin
if (a is Option<i32>.some) {
    println("some " + cast<string>(a.some));
} else if (a is Option<i32>.none) {
    println("none");
}
```
