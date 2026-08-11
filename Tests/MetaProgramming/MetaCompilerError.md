# MetaCompilerError
## Description
`compiler().error("compiler bad value")` emits an Error-severity compile-time diagnostic via `penguin_meta_error` → `report_error`, FAILING compilation (same as `#error`). Negative test: compile exit code 1, no Run section. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun err_probe() -> i64 {
    compiler().error("compiler bad value");
    return 0;
}
initial {
    let x = #err_probe();
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
