# FunctionCallMutabilityTest
## Description
Immutable arg accepted where immutable param expected; mutable arg where mutable expected.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : Box<i32> = new Box<i32>(1);
    let b : mut Box<i32> = new Box<i32>(2);
    let c : mut Box<i32> = new Box<i32>(2);
    foo(a, b, c);
}

fun foo(a: Box<i32>, b: mut Box<i32>, c: Box<i32>) {
    print(cast<string>(a.value));
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
