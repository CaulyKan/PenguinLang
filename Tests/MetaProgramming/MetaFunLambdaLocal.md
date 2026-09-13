# MetaFunLambdaLocal
## Description
A lambda inside a `#fun` body, bound to a fun-typed local and called indirectly: the meta unit B compiles the closure through the full pipeline (synthesized `__lambda_<n>` class, indirect `__FunVal` dispatch) under the LLVM ORC JIT, and the scalar result splices into runtime code. `f(5) + f(4)` = `5*3 + 4*3` = 27.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun tri(n: i64) -> i64 {
    let f: fun<i64, i64> = fun (x: i64) -> i64 { return x * 3; };
    return f(n) + f(n - 1);
}
initial {
    println(cast<string>(#tri(5)));
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
ExpectedStdout: EQUALS `27
`
ExpectedStderr: DISCARD
