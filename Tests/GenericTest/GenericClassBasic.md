# GenericClassBasic
## Description
Generic class with single type parameter, basic get/set.

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
        let b = new Box<i32>(42);
        println(cast<string>(b.get()));
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
ExpectedStdout: EQUALS `42
`
ExpectedStderr: DISCARD
