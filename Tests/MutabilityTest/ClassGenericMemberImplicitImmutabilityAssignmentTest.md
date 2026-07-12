# ClassGenericMemberImplicitImmutabilityAssignmentTest
## Description
`mut A<i32>` binding does not make an immutable `T` field assignable.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class A {
    a: T;
}
initial {
    let a : mut A<i32> = new A<i32>();
    a.a = 1;
    print(cast<string>(a.a));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
