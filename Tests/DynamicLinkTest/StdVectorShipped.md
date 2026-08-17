# StdVectorShipped
## Description
Build a `.penguin-lib` shipping `std.Vector<i64>`: `vector.penguin` marks `export class Vector<T>`, and the lib's own code instantiates `Vector<i64>` (the seed) so the monomorphizer ships that instance + its closure. The consumer uses the SHIPPED instance — it DECLARES `std.Vector__i64`'s methods (declare-not-define) and links to the lib's symbols at runtime. Exercises the core dyn-lib path: lib build (-shared + JSON metadata + PENGUINLIB footer), consumer `--lib` load (embedded source merge), shipped-instance declare routing, and `-rdynamic` symbol export. Pass3-only.

## Apply To
* EmperorPenguin Pass3

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
initial {
    let mut v = new std.Vector<i64>();
    v.push(10);
    v.push(20);
    println("size=" + cast<string>(v.size()));
    println("at1=" + cast<string>(v.at(1).some));
    let sum: mut i64 = 0;
    for (let x in v) { sum = sum + x; }
    println("sum=" + cast<string>(sum));
}
```
## Build 2
Args: `--lib ${WORKDIR}/std.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `size=2
at1=20
sum=30
`
ExpectedStderr: DISCARD
