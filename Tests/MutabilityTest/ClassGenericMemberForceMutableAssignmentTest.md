# ClassGenericMemberForceMutableAssignmentTest
## Description
Field forced `mut T` is assignable even via an immutable binding.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class A {
    a: mut T = 0;
}
initial {
    let a : A<i32> = new A<i32>();
    a.a = 1;
    print(cast<string>(a.a));
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
