# ClassImmutableAssignmentErrorTest
## Description
Writing an immutable field through an immutable binding must fail.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class A {
    a: i32 = 0;
}
initial {
    let a : A = new A();
    a.a = 1;
    print(cast<string>(x));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
