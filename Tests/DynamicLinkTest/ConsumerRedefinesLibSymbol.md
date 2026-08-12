# ConsumerRedefinesLibSymbol
## Description
A consumer that REDEFINES a name a loaded lib ships (both define `foo.answer`, which the lib EXPORTS) is a hard ERROR: the semantic layer detects the duplicate definition (`E_DUPLICATE_SYMBOL`, "consumers may not redefine it") instead of silently shadowing. Pass4-only.

## Apply To
* EmperorPenguin Pass4

## Test Code
```
namespace foo {
    export fun answer() -> i64 { return 42; }
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
namespace foo {
    fun answer() -> i64 { return 1; }
}
initial {
    println("consumer");
}
```
## Build 2
Args: `--lib ${WORKDIR}/foo.penguin-lib`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `duplicate`
