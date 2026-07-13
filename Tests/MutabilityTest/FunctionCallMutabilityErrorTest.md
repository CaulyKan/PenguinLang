# FunctionCallMutabilityErrorTest
## Description
Passing an immutable arg to a `mut` param (and undefined args) must fail.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : Box<i32> = new Box<i32>(1);
    foo(a, b, c);
}

fun foo(a: mut Box<i32>) {
    print(cast<string>(a.value));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_RESOLVE_SYMBOL`
