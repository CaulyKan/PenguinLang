# ReferenceTypeRejectsImmToMut
## Description
A class implementing IReferenceType must reject imm→mut assignment. EmperorPenguin allows it at compile time and expects the runtime error path.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Ref {
        x: i32;
        impl IReferenceType;
        fun new(mut this, x: i32) {
            this.x = x;
        }
    }
    initial {
        let a = new Ref(1);
        let b: mut Ref = a;
        println("error_expected");
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
ExpectedStdout: EQUALS `error_expected
`
ExpectedStderr: DISCARD
