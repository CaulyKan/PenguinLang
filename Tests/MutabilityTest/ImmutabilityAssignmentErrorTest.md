# ImmutabilityAssignmentErrorTest
## Description
Assigning to an immutable local must fail to compile.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let x: i32 = 1;
    x += 1;
    print(cast<string>(x));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
