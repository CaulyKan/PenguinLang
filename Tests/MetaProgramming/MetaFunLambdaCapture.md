# MetaFunLambdaCapture
## Description
A lambda inside a `#fun` body capturing the `#fun`'s parameter and a local: closure capture analysis + snapshot fields run inside meta unit B (the closure object is allocated by JIT-executed code; only the scalar result crosses back). `f(10) + f(20)` with `n=7` and `base=n+100=107` = `124 + 134` = 258.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun mkadd(n: i64) -> i64 {
    let base: i64 = n + 100;
    let f: fun<i64, i64> = fun (x: i64) -> i64 { return x + n + base; };
    return f(10) + f(20);
}
initial {
    println(cast<string>(#mkadd(7)));
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
ExpectedStdout: EQUALS `258
`
ExpectedStderr: DISCARD
