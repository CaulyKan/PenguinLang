# CascadeClassMutableMemberAssignmentTest2
## Description
Cascade write succeeds because the inner field is mutable (auto via binding).

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
    a: mut i32 = 0;
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
