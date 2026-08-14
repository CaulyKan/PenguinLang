# MetaTemplateValueBox
## Description
A value-template function that builds and returns a runtime REFERENCE type (`Box<i64>`) from the integer value param N. Under D6 value-template functions are specialized at runtime (`filled<3>()` → `filled__3`) instead of desugaring to compile-time `#fun`s — whose i64-trampoline ABI could not return/splice a reference type. The specialized body substitutes `N` → `3` and computes a `Box<i64>` in a `while` loop; the caller reads `b.value` to prove the heap reference round-trips correctly.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
fun filled() -> Box<i64> {
    let total: mut i64 = 0;
    let i: mut i64 = 0;
    while (i < N) {
        total = total + i;
        i = i + 1;
    }
    return new Box<i64>(total);
}
initial {
    let b = filled<3>();
    println("value=" + cast<string>(b.value));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `value=3
`
ExpectedStderr: DISCARD
