# GenericClassFieldAccess
## Description
Generic class with field of another generic type.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    #template(T: type)
    class Foo {
        value: T;
        fun new(mut this, v: T) {
            this.value = v;
        }
    }
    #template(T: type)
    class Bar {
        value: Foo<T>;
        fun new(mut this, v: T) {
            this.value = new Foo<T>(v);
        }
    }
    initial {
        let b = new Bar<i32>(99);
        println(cast<string>(b.value.value));
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
ExpectedStdout: EQUALS `99
`
ExpectedStderr: DISCARD
