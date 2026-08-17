# NonGenericExportedFunction
## Description
A lib with only NON-generic `export fun` declarations (`foo.answer()` / `foo.double()`, marked `export`). The consumer declares them (is_lib_export via the merged lib source) and links to the lib symbols at runtime — no generics involved, exercising the plain function-over-lib-boundary path + the `export` keyword. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace foo {
    export fun answer() -> i64 { return 42; }
    export fun double(v: i64) -> i64 { return v * 2; }
}
```
## Build 1
Kind: lib
Name: foo.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    println("answer=" + cast<string>(foo.answer()));
    println("double=" + cast<string>(foo.double(21)));
}
```
## Build 2
Args: `--lib ${WORKDIR}/foo.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `answer=42
double=42
`
ExpectedStderr: DISCARD
