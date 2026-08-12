# MetaFunDirectCall
## Description
A lib DEFINES a `#fun dbl(x: i64) -> i64` (compile-time meta function). The consumer calls `#dbl(21)` DIRECTLY — the meta call JIT-executes the LIB's `#fun` (whose body travels verbatim in the lib's embedded source) and splices the result 42 at compile time. Verifies a `#fun` defined in a lib is callable from the exe without any runtime symbol. Pass4-only.

## Apply To
* EmperorPenguin Pass4

## Test Code
```
#fun dbl(x: i64) -> i64 { return x * 2; }
```
## Build 1
Kind: lib
Name: mfun.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let r: i64 = #dbl(21);
    println("dbl=" + cast<string>(r));
    let r2: i64 = #dbl(100);
    println("dbl2=" + cast<string>(r2));
}
```
## Build 2
Args: `--lib ${WORKDIR}/mfun.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `dbl=42
dbl2=200
`
ExpectedStderr: DISCARD
