# MetaWarn
## Description
R1 diagnostics + string args: `#warn(msg)` inside a `#fun` with a **string parameter** — `#warn_test("deprecation")` passes a compile-time string literal through `bind_meta_arg_value` (string branch → `register_string_value` → token → caller-stub `penguin_meta_get_string_value` → ptr → #fun receives `string`). The `#warn` fires via the host callback `penguin_meta_warn` → `report_warning` (doesn't fail compilation). Tests both string-arg support and diagnostic emission. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun warn_test(msg: string) -> i64 {
    #warn(msg);
    return 42;
}
initial {
    println("result=" + cast<string>(#warn_test("deprecation")));
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
ExpectedStdout: EQUALS `result=42
`
ExpectedStderr: DISCARD
