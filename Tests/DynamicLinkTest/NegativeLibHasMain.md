# NegativeLibHasMain
## Description
A `.penguin-lib` build whose source contains an `initial{}` routine is rejected: a lib may not emit an `@main` entry point (validate_lib_defs). Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("lib must not have main");
}
```
## Build 1
Kind: lib
Name: bad.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `initial`
