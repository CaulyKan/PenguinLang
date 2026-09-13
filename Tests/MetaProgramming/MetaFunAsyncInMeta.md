# MetaFunAsyncInMeta
## Description
Negative: coroutine constructs are not available inside metaprogramming — the `#fun` body compiles as meta unit B with `enable_coroutine` off (the JIT process never wires a scheduler), so an `async` spawn (or any wait form) inside a `#fun` must fail compilation with the require_coroutine E_UNSUPPORTED gate — deterministically, not crash or silently misbehave at JIT time. Lambdas and fun values themselves ARE legal in `#fun` bodies (see MetaFunLambdaLocal); only the coroutine surface is out.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
async fun later(n: i64) -> i64 { return n + 1; }
#fun bad(n: i64) -> i64 {
    let f = async later(n);
    wait f;
    return n;
}
initial {
    println(cast<string>(#bad(1)));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
