# ClassGenericMemberAssignmentErrorTest
## Description
Generic field instantiated as immutable `i32` is not assignable.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class A {
    a: T;
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
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_MUTABILITY`
