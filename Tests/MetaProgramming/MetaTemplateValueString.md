# MetaTemplateValueString
## Description
req3/req4 — a non-type (value) template parameter of type `string`. `#template<S: string> fun slen()` is specialized at runtime (D6): `slen<"hello">()` → `slen__hello`, whose body substitutes `S` → `"hello"` and calls `string_length` at runtime. Exercises a value-template type other than i32.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(S: string)
fun slen() -> i64 {
    return string_length(S);
}
initial {
    println("len=" + cast<string>(slen<"hello">()));
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
ExpectedStdout: EQUALS `len=5
`
ExpectedStderr: DISCARD
