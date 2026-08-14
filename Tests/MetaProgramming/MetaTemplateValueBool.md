# MetaTemplateValueBool
## Description
req3/req4 — a non-type (value) template parameter of type `bool`. `#template<B: bool> fun flag()` is specialized at runtime (D6: value-template functions are no longer desugared to compile-time `#fun`s). The call site `flag<true>()` / `flag<false>()` specializes `flag__true` / `flag__false`, substituting the bool literal into the body — `if (B)` becomes `if (true)`. Exercises a value-template type other than i32.

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
