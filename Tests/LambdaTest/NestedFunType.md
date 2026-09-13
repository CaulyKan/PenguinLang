# NestedFunType
## Description
A `fun<...>` type used in a NESTED generic-argument position (`Option<fun<i32, i32>>`): parsing (fun-type args land in `function_params`, arg 0 = return type), type resolution, storing a function reference in the Option payload, extracting it with try-bind and calling through it. BabyPenguin already supports this (ANTLR grammar allows the fun-type alternative inside genericArguments); this locks the same behavior in on EmperorPenguin.

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
    let o : Option<fun<i32, i32>> = new Option<fun<i32, i32>>.some(twice);
    if (let f := o.some) {
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
