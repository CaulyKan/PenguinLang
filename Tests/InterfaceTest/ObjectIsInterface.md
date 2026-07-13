# ObjectIsInterface
## Description
Test `is InterfaceType` operator: checks if an object implements an interface. Requires EmperorPenguin.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    interface IShow {}
    class Point {
        x: i32;
        fun new(mut this, x: i32) { this.x = x; }
        impl IShow {}
    }
    class NoShow {
        fun new(mut this) {}
    }
    initial {
        let p = new Point(1);
        let n = new NoShow();
        println(cast<string>(p is IShow));
        println(cast<string>(n is IShow));
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
