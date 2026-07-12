# GenericClassTwoInstancesSameType
## Description
Two instances of generic class with same type parameter.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    #template(T: type)
    class Box {
        value: T;
        fun new(mut this, v: T) {
            this.value = v;
        }
        fun get(this) -> T {
            return this.value;
        }
    }
    initial {
        let b1 = new Box<i32>(10);
        let b2 = new Box<i32>(20);
        println(cast<string>(b1.get()));
        println(cast<string>(b2.get()));
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
ExpectedStdout: EQUALS `10
20
`
ExpectedStderr: DISCARD
