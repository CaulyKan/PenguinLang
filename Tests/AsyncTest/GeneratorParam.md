# GeneratorParam
## Description
A generator function with PARAMETERS plus a mutable local loop counter: the parameters must flow into the generator's body (captured at construction — `gparam(5)` bakes n=5 into every yield) and `while` bodies must carry yields. Both compilers implement this via snapshot captures: EmperorPenguin's synthesized __GenCtx ctx class captures the parameters as snapshot fields; BabyPenguin's RewriteGenerator passes them as AddLambdaClass CAPTURE parameters (baked by the ctor, body references rewritten to this.<name>, `call` takes only `this`). This used to be a RED SENTINEL for BabyPenguin (params fed the `call` signature instead — `owner.call` typed `fun<i64,i64>` and `cast<IGenerator<T>>(new _DefaultRoutine<T>(...))` failed with E_CAST_INVALID) and turned green when the capture form landed; it now locks the parameterized-generator behavior in on both compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
fun gparam(n: i64) -> IGenerator<i64> {
    yield n;
    let i: mut i64 = 0;
    while (i < 2) {
        yield n + i + 1;
        i = i + 1;
    }
    return n * 100;
}
initial {
    for (let v : i64 in gparam(5)) {
        print(cast<string>(v) + ",");
    }
    println("");
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `5,6,7,500,
`
ExpectedStderr: DISCARD
