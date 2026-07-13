# ClassGenericMemberForceMutableImcompatibleErrorTest2
## Description
`!mut T` field is not assignable even via a mutable `mut i32` arg.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class A {
    a: !mut T = 0;
}
initial {
    let a : mut A<mut i32> = new A<mut i32>();
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
