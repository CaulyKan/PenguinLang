# AutoICopyForValueType
## Description
IValueType class without explicit ICopy should auto-generate ICopy.

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
        let a : mut Point = new Point();
        a.x = 1;
        a.y = 2;
        let b : mut Point = a.copy();  // auto-generated ICopy
        print(cast<string>(b.x));
        print(cast<string>(b.y));
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
