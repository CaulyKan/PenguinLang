# FunValueGcStress
## Description
GC safety of fun values: a function reference stored in an `Option` (enum) payload must survive garbage collections triggered by intervening heap allocations — on EmperorPenguin the fun value is a callable-object pointer, and this locks that the enum payload slot is traced (the conservative/precise GC keeps the callable object alive) and the call still dispatches afterwards.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun twice(x: i32) -> i32 {
    return x * 2;
}
initial {
    let keep : mut Option<fun<i32, i32>> = new Option<fun<i32, i32>>.some(twice);
    let i : mut i64 = 0;
    while (i < 200000) {
        let garbage : mut StringBuilder = new StringBuilder();
        garbage.append("x");
        i = i + 1;
    }
    if (let f := keep.some) {
        println(cast<string>(f(21)));
    }
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
ExpectedStdout: EQUALS `42
`
ExpectedStderr: DISCARD
