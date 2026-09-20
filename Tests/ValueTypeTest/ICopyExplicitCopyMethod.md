# ICopyExplicitCopyMethod

## Description

The accepted spelling for `impl ICopy<Self>` on a class with reference-typed
fields: provide an explicit `fun copy(this) -> Self` method. `.copy()` then
resolves to the user method (member lookup wins over the empty vtable slot —
the SourceLocation pattern inside the compiler itself), so the copy is as
deep as the user wrote it. Verified on BabyPenguin (VM + CS backend) and
EmperorPenguin Pass1/Pass2/Pass3: `b.i` is a FRESH Inner, `b.i.x = 7` is
invisible through `a` (prints 0).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Inner {
    impl IReferenceType;
    x: i64 = 0;
}
class Outer {
    impl ICopy<Self>;
    i: Inner;

    fun copy(this) -> Outer {
        let n : mut Outer = new Outer();
        n.i = new Inner();
        n.i.x = this.i.x;
        return n;
    }
}
initial {
    let a : mut Outer = new Outer();
    a.i = new Inner();
    let b : mut Outer = a.copy();
    b.i.x = 7;
    print(cast<string>(a.i.x));
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
ExpectedStdout: EQUALS `0`
ExpectedStderr: DISCARD
