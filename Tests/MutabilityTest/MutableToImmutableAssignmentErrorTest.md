# MutableToImmutableAssignmentErrorTest
## Description
Assigning (not initializing) a mutable value to an immutable binding must fail.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : mut Box<i32> = new Box<i32>(1);
    let b : Box<i32>;
    b = a;
    print(cast<string>(b.value));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_MUTABILITY`
