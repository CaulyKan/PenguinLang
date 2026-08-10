# StdArrayTest
## Description
End-to-end test of the meta-programming-driven stdlib `penguin.Array<T,N>` (fixed-size contiguous array) from `EmperorPenguin/std/penguin/array.penguin`. Allocates an `Array<i32,5>` via `_malloc(N*#sizeof(T))` (#sizeof computed at compile time = 4), fills it with `set`, reads back with bounds-checked `at` (out-of-bounds → Option.none), checks `size`, and manually `dispose_mem()`s. Exercises the new pointer IR (`#__load`/`#__store` → LOAD_PTR/STORE_PTR), the `#sizeof` compile-time intrinsic, the `_malloc`/`_mfree` externs, and req5 mixed type+value class templates (`#template<T: type, N: u64>`). Pass3-only: array.penguin is bootstrap-deferred stdlib (not auto-loaded; passed via Compile.Args) and the pointer IR is EmperorPenguin-native only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a = new penguin.Array<i32, 5>();
    let i: mut u64 = 0;
    while (i < a.size()) {
        a.set(i, cast<i32>(i) * cast<i32>(i));
        i = i + 1;
    }
    println("a[0]=" + cast<string>(a.at(cast<u64>(0)).some));
    println("a[3]=" + cast<string>(a.at(cast<u64>(3)).some));
    println("a[4]=" + cast<string>(a.at(cast<u64>(4)).some));
    if (a.at(cast<u64>(99)).is_none()) {
        println("a[99]=<oob>");
    }
    println("size=" + cast<string>(a.size()));
    let sum: mut i32 = 0;
    for (let x in a) {
        sum = sum + x;
    }
    println("sum=" + cast<string>(sum));
    a.dispose_mem();
    println("disposed");
}
```

## Compile
Args: `EmperorPenguin/std/penguin/array.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a[0]=0
a[3]=9
a[4]=16
a[99]=<oob>
size=5
sum=30
disposed
`
ExpectedStderr: DISCARD
