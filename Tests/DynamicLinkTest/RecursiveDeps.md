# RecursiveDeps
## Description
Three-stage build verifying recursive dependency loading: Build 1 ships `std.penguin-lib` (Vector<i64>); Build 2 ships `mid.penguin-lib` — `namespace mid` referencing `std.Vector<i64>`, built WITH `--lib std.penguin-lib` (its metadata records `deps: ["std"]`, and mid's .so has undefined std refs, NOT linked); Build 3 is an exe given ONLY `--lib mid.penguin-lib` — the loader recursively resolves and loads `std` (deps-first). Pass4-only.

## Apply To
* EmperorPenguin Pass4

## Test Code
```
fun __force_std_exports() {
    let _v = new std.Vector<i64>();
}
```
## Build 1
Kind: lib
Name: std.penguin-lib
Args: `EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
namespace mid {
    fun first(v: std.Vector<i64>) -> i64 {
        return v.at(0).some;
    }
    fun answer() -> i64 { return 99; }
}
```
## Build 2
Kind: lib
Name: mid.penguin-lib
Args: `--lib ${WORKDIR}/std.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let mut v = new std.Vector<i64>();
    v.push(5);
    v.push(7);
    let s: i64 = mid.first(v);
    println("first=" + cast<string>(s));
    println("answer=" + cast<string>(mid.answer()));
}
```
## Build 3
Args: `--lib ${WORKDIR}/mid.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `first=5
answer=99
`
ExpectedStderr: DISCARD
