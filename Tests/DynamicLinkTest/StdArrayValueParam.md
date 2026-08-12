# StdArrayValueParam
## Description
Build a `.penguin-lib` shipping `std.Array<i32,5>` — a mixed type+value class template (`export class Array<T,N>` using `#sizeof` + pointer IR). A seed instantiates `Array<i32,5>`; the consumer uses the shipped instance, exercising the value-template-param path across the lib boundary. Pass4-only.

## Apply To
* EmperorPenguin Pass4

## Test Code
```
fun __force_std_exports() {
    let _a = new std.Array<i32, 5>();
}
```
## Build 1
Kind: lib
Name: std.penguin-lib
Args: `EmperorPenguin/std/penguin/array.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let a = new std.Array<i32, 5>();
    let i: mut i32 = 0;
    while (i < 5) { a.set(cast<u32>(i), i * 3); i = i + 1; }
    println("size=" + cast<string>(a.size()));
    println("at4=" + cast<string>(a.at(4).some));
    if (a.at(9).is_none()) { println("at9=none"); }
    let sum: mut i64 = 0;
    let j: mut i32 = 0;
    while (j < 5) { sum = sum + cast<i64>(a.at(cast<u32>(j)).some); j = j + 1; }
    println("sum=" + cast<string>(sum));
    a.dispose_mem();
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
ExpectedStdout: EQUALS `size=5
at4=12
at9=none
sum=30
`
ExpectedStderr: DISCARD
