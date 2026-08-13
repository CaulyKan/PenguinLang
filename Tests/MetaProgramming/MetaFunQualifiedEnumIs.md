# MetaFunQualifiedEnumIs
## Description
A qualified generic enum-variant `is` check inside a `#fun` body (compiled via the meta unit-B JIT) lowers correctly to an ISENUM tag comparison. `o is __builtin.Option<i64>.some` returns true for a `some` value and false for `none`. Exercises the namespace-qualified + generic-args enum variant form on the `is` RHS through the `#fun` JIT path (a `#fun` cannot take an `Option<T>` parameter directly — meta params are only type/ast/i64/bool/string — so the concrete `__builtin.Option<i64>` is constructed inside the body).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun check_some() -> bool {
    let o = new __builtin.Option<i64>.some(42);
    return o is __builtin.Option<i64>.some;
}
#fun check_none() -> bool {
    let o = new __builtin.Option<i64>.none();
    return o is __builtin.Option<i64>.some;
}
initial {
    println("some=" + cast<string>(#check_some()));
    println("none=" + cast<string>(#check_none()));
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
ExpectedStdout: EQUALS `some=true
none=false
`
ExpectedStderr: DISCARD
