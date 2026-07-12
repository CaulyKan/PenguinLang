# ExplicitIValueTypeClass
## Description
Class with explicit IValueType (even with string field) is treated as value type.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Val {
        x: i32;
        y: string;
        impl IValueType;
    }
    initial {
        let a : Val = new Val();
        let b : mut Val;
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
