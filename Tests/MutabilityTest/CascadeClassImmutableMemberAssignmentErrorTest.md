# CascadeClassImmutableMemberAssignmentErrorTest
## Description
Cascade write to an immutable nested field must fail.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class B {
    a : A = new A();
}
class A {
    a: i32 = 0;
}
initial {
    let b : B = new B();
    b.a.a = 1;
    print(cast<string>(b.a.a));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
