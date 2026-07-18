# GenericClassNestedArg
## Description
Nested generic: Box<Box<i32>>.

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
        let inner = new Box<i32>(42);
        let outer = new Box<Box<i32>>(inner);
        println(cast<string>(outer.get().get()));
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
