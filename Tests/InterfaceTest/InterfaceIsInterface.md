# InterfaceIsInterface
## Description
Test `is InterfaceType` between derived and base interfaces. Requires EmperorPenguin for interface instance checks.

## Apply To
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    interface IBase {
        fun new(mut this) {}
    }
    interface IDerived {
        fun new(mut this) {}
    }
    class Impl {
        fun new(mut this) {}
        impl IBase {}
        impl IDerived {}
    }
    initial {
        let obj: IBase = cast<IBase>(new Impl());
        println(cast<string>(obj is IDerived));
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
`
ExpectedStderr: DISCARD
