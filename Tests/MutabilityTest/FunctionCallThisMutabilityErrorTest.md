# FunctionCallThisMutabilityErrorTest
## Description
Calling a `mut this` method on an immutable binding must fail.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class A {
    a: i32 = 1;
    fun immutable(this) {
        print(cast<string>(this.a));
    }
    fun mutable(mut this) {
        print(cast<string>(this.a));
    }
}
initial {
    let a : A = new A();
    a.mutable();
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_TYPE_MISMATCH`
