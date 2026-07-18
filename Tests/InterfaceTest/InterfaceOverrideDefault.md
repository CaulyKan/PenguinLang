# InterfaceOverrideDefault
## Description
Interface with default method overridden in implementation class (EmperorPenguin-only feature).

## Apply To
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    interface IGreet {
        fun new(mut this) {}
        fun greet(this) -> string {
            return "hello";
        }
    }
    class Bar {
        fun new(mut this) {}
        impl IGreet {
            fun greet(this) -> string {
                return "hi from Bar";
            }
        }
    }
    initial {
        let b = new Bar();
        println(b.greet());
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
ExpectedStdout: EQUALS `hi from Bar
`
ExpectedStderr: DISCARD
