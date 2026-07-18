# AutoValueTypeClassAssignment
## Description
Class with all value-type fields auto-implements IValueType; imm→mut copy works.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Point {
        x: i32;
        y: i32;
    }
    initial {
        let a : Point = new Point();
        let b : mut Point;
        b = a;
        print("ok");
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
ExpectedStdout: EQUALS `ok`
ExpectedStderr: DISCARD
