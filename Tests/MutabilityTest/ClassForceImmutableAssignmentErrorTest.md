# ClassForceImmutableAssignmentErrorTest
## Description
Writing a `!mut` field must fail even through a mutable binding.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class A {
    a: !mut i32 = 0;
}
initial {
    let a : mut A = new A();
    a.a = 1;
    print(cast<string>(x));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_MUTABILITY`
