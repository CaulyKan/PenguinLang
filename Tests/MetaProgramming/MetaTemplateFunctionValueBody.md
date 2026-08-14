# MetaTemplateFunctionValueBody
## Description
A value-template FUNCTION with a regular parameter — the D6 runtime-generic full form. `#template(N: i32) fun scaled(x: i32)` — the call `scaled<5>(3)` specializes to `scaled__5` (value param N substituted into the body: `return x * N` → `return x * 5`) and is a normal runtime call → `15`. Unlike the compile-time-constexpr value-template functions (which had no regular params), this verifies the value param substitutes into a function body that also takes runtime arguments. Verified on native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
fun scaled(x: i32) -> i32 {
    return x * N;
}
initial {
    println("r=" + cast<string>(scaled<5>(3)));
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
ExpectedStdout: EQUALS `r=15
`
ExpectedStderr: DISCARD
