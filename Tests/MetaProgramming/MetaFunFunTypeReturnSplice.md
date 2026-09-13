# MetaFunFunTypeReturnSplice
## Description
Negative: a `#fun` with a FUNCTION-typed return (`-> fun<i64, i64>`) spliced in expression position must fail with E_UNSUPPORTED. The fun-type return specifier parses with `is_function_type` and an EMPTY name, which historically slipped past the reference-return guard (the empty kind fell through to the integer-literal splice and emitted the fun object's runtime address as an integer). The guard now treats a function-typed return exactly like a reference return: fun values cannot cross the meta boundary; use it inside another `#fun`.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun mk() -> fun<i64, i64> {
    return fun (x: i64) -> i64 { return x; };
}
initial {
    let g: fun<i64, i64> = #mk();
    println(cast<string>(g(1)));
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
