# MutexBasic
## Description

Minimal `Mutex<T>` surface (two-tier heap P6/D13): with/get/set on scalar and reference payloads, nested with on distinct mutexes, instance isolation, and whole-payload replacement via set. Reference payloads are mutated through the `mut T` callback alias (the v1 "small & hot mutable" tier — a `fun<void, mut T>` callback); scalar payloads are value-copied into the callback, so writes go through `set()` (the value-semantics tier). BabyPenguin's mirror is lock-free (single-threaded VM); EmperorPenguin runs a real pthread_mutex bracket around the whole critical section, callback included (the shape M2's write-point promotion hooks into). The payload constructor and set both take `mut T` — an immutable alias cannot enter a mutable payload slot (alias-leak protection).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Counter {
    impl IReferenceType;
    n: mut i64 = 0;
}

initial {
    // scalar payload: with sees a copy (read-only), set writes back
    let m: mut __builtin.Mutex<i64> = new __builtin.Mutex<i64>(5);
    m.with(fun(v: mut i64) -> void { println("scalar read=" + cast<string>(v)); });
    m.set(41);
    println("scalar set/get=" + cast<string>(m.get()));

    // reference payload: with mutates the object through the alias
    let c: mut Counter = new Counter();
    let m2: mut __builtin.Mutex<Counter> = new __builtin.Mutex<Counter>(c);
    m2.with(fun(v: mut Counter) -> void { v.n = v.n + 10; });
    println("ref with=" + cast<string>(m2.get().n) + " direct=" + cast<string>(c.n));

    // nested with on distinct mutexes: no deadlock by construction
    let m3: mut __builtin.Mutex<i64> = new __builtin.Mutex<i64>(1);
    m2.with(fun(v: mut Counter) -> void {
        m3.set(v.n);
    });
    println("nested=" + cast<string>(m3.get()));

    // instance isolation
    let a: mut __builtin.Mutex<i64> = new __builtin.Mutex<i64>(100);
    let b: mut __builtin.Mutex<i64> = new __builtin.Mutex<i64>(200);
    a.set(a.get() + 1);
    println("iso=" + cast<string>(a.get()) + "/" + cast<string>(b.get()));

    // whole-payload replacement via set (mut binding required)
    let c2: mut Counter = new Counter();
    c2.n = 7;
    m2.set(c2);
    println("swap=" + cast<string>(m2.get().n));
}
```

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `scalar read=5
scalar set/get=41
ref with=10 direct=10
nested=10
iso=101/200
swap=7
`
ExpectedStderr: DISCARD
