# MetaTemplateValueRecursion
## Description
req3 (meta x template): a non-type (value) template parameter on a function, evaluated at compile time via desugar-to-#fun. `#template<N:i32> fun fab()` desugars to `#fun fab(N:i64)`; the recursive body calls `fab<N-1>()` (value-generic call) which inside the #fun body bind as plain recursive calls, and the host call site `fab<5>()` splices as a meta call `#fab(5)` JIT-evaluated to the fibonacci-like value 5. Requires native Pass2/Pass3 (meta JIT).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
fun fab() -> i64 {
    if (N < 2) { return N; }
    return fab<N-1>() + fab<N-2>();
}
initial {
    let r = fab<5>();
    println("fab=" + cast<string>(r));
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
ExpectedStdout: EQUALS `fab=5
`
ExpectedStderr: DISCARD
