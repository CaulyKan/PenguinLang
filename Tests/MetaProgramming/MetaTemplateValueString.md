# MetaTemplateValueString
## Description
req3/req4 — a non-type (value) template parameter of type `string`. `#template<S: string> fun slen()` desugars to `#fun slen(S: string)`; the call site `slen<"hello">()` splices the string literal as the meta argument (kind `string` path in bind_meta_arg_value → register_string_value). Exercises a value-template type other than i32. Requires native Pass2/Pass3 (meta JIT).

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
