# CascadeEnumMutableMemberAssignmentTest
## Description
Cascade write through a mutable enum variant payload to a mutable field.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
enum B {
    a : A;
    b;
}
class A {
    a: mut i32 = 0;
}
initial {
    let b : mut B = new B.a(new A());
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
