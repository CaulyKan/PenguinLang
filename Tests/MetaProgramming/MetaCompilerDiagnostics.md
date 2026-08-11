# MetaCompilerDiagnostics
## Description
Compile-time diagnostics via the `#compiler()` facade: `compiler().warn("compiler warn")` and `compiler().info("compiler info")` route to `report_warning`/`report_info` (neither fails compilation). The `#fun` returns 7, so the program compiles and prints `d=7`. Same channel as `#warn`/`#info` (shared `active_model`). The warning/info text goes to the compiler's diagnostic stream (not the program stdout). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun diag_probe() -> i64 {
    compiler().warn("compiler warn");
    compiler().info("compiler info");
    return 7;
}
initial {
    println("d=" + cast<string>(#diag_probe()));
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
ExpectedStdout: EQUALS `d=7
`
ExpectedStderr: DISCARD
