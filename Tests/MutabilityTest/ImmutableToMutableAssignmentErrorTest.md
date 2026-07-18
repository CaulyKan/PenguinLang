# ImmutableToMutableAssignmentErrorTest
## Description
Assigning an immutable reference to a mutable binding must fail.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : Box<i32> = new Box<i32>(1);
    let b : mut Box<i32>;
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
