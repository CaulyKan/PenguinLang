# HigherOrderFunTest
## Description
A fun value passed as a FUNCTION ARGUMENT and called inside the callee (`apply(twice, 21)`) — exercises the parameter-symbol call path (identifier callee that is a fun-typed local) and the funptr const at an argument position.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun twice(x: i32) -> i32 {
    return x * 2;
}
fun apply(f: fun<i32, i32>, v: i32) -> i32 {
    return f(v);
}
initial {
    println(cast<string>(apply(twice, 21)));
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
