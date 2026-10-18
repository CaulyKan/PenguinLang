# InterfaceVarIsRuntime
## Description
`x is I` where x is INTERFACE-typed keeps the runtime check (the primitive-interfaces round only folds statically-known sealed types). The runtime dispatch answers by the object's actual vtable: true for the concrete class that implements the interface, false for one that does not.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
interface IVal { fun v(this) -> i64; }
interface IOther { fun q(this) -> i64; }

class A {
    fun new(mut this) {}
    impl IReferenceType;
    impl IVal { fun v(this) -> i64 { return 1; } }
}

class B {
    fun new(mut this) {}
    impl IReferenceType;
    impl IOther { fun q(this) -> i64 { return 2; } }
}

initial {
    let x: IVal = cast<IVal>(new A());
    println(cast<string>(x is IVal));
    println(cast<string>(x is IOther));
    let y: IVal = cast<IVal>(new A());
    let z: IOther = cast<IOther>(new B());
    println(cast<string>(z is IOther));
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
ExpectedStdout: EQUALS `true
false
true
`
ExpectedStderr: DISCARD
