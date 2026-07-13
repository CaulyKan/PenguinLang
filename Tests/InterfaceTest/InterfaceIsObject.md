# InterfaceIsObject
## Description
Interface identity check via `is ClassType` after casting to interface.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
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
    class Cat {
        fun new(mut this) {}
        impl IAnimal {
            fun speak(this) -> string {
                return "meow";
            }
        }
    }
    initial {
        let d: IAnimal = cast<IAnimal>(new Dog());
        if (d is Dog) {
            println("is dog");
        } else {
            println("not dog");
        }
        if (d is Cat) {
            println("is cat");
        } else {
            println("not cat");
        }
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
ExpectedStdout: EQUALS `is dog
not cat
`
ExpectedStderr: DISCARD
