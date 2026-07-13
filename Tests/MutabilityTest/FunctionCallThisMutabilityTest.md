# FunctionCallThisMutabilityTest
## Description
`this`/`mut this` methods callable per binding mutability; outputs 111.

## Apply To
* BabyPenguin
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
    a.immutable();
    let b: mut A = new A();
    b.mutable();
    b.immutable();
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
ExpectedStdout: EQUALS `111`
ExpectedStderr: DISCARD
