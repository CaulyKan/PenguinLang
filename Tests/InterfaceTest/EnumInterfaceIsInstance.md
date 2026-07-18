# EnumInterfaceIsInstance
## Description
Test `is InterfaceType` on enum variants. Requires EmperorPenguin.

## Apply To
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    interface IShow {}
    enum Color {
        Red;
        impl IShow {}
    }
    enum Size {
        Big;
    }
    initial {
        let c = new Color.Red();
        let s = new Size.Big();
        println(cast<string>(c is IShow));
        println(cast<string>(s is IShow));
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
`
ExpectedStderr: DISCARD
