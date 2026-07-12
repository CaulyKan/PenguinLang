# GenericClassFunction
## Description
Generic function on a non-generic class.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    class Foo {
        #template(T: type)
        fun identity(this, x: T) -> T {
            return x;
        }
    }
    initial {
        let a = new Foo();
        let result: string = a.identity<string>("hello");
        println(result);
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
