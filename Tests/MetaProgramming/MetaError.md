# MetaError
## Description
R1 diagnostics: `#error("bad value")` inside a `#fun` body emits a compile-time ERROR via `penguin_meta_error` → `report_error`. The error IS Error-severity → compilation FAILS with `error[E_META]: bad value`. This is a negative test (compile failure expected). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun err_test() -> i64 {
    #error("bad value");
    return 0;
}
initial {
    let x = #err_test();
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
