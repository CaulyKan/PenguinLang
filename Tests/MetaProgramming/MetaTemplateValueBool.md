# MetaTemplateValueBool
## Description
req3/req4 — a non-type (value) template parameter of type `bool`. `#template<B: bool> fun flag()` desugars to `#fun flag(B: bool)`; the call site `flag<true>()` / `flag<false>()` splices the bool literal as the meta argument. Exercises a value-template type other than i32 (the meta param kind `bool` path in bind_meta_arg_value). Requires native Pass2/Pass3 (meta JIT).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(B: bool)
fun flag() -> i64 {
    if (B) { return 1; }
    return 0;
}
initial {
    println("t=" + cast<string>(flag<true>()));
    println("f=" + cast<string>(flag<false>()));
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
ExpectedStdout: EQUALS `t=1
f=0
`
ExpectedStderr: DISCARD
