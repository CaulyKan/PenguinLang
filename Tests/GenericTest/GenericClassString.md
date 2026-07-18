# GenericClassString
## Description
Generic class with string type parameter.

## Apply To
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
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
        let b = new Box<string>("hello");
        println(b.get());
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
ExpectedStdout: EQUALS `hello
`
ExpectedStderr: DISCARD
