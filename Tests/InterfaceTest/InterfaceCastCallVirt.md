# InterfaceCastCallVirt
## Description
Cast object to interface and call virtual method.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    interface IAnimal {
        fun new(mut this) {}
        fun speak(this) -> string;
    }
    class Dog {
        fun new(mut this) {}
        impl IAnimal {
            fun speak(this) -> string {
                return "woof";
            }
        }
    }
    initial {
        let d: IAnimal = cast<IAnimal>(new Dog());
        println(d.speak());
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
ExpectedStdout: EQUALS `woof
`
ExpectedStderr: DISCARD
