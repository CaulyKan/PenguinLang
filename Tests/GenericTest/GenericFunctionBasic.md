# GenericFunctionBasic
## Description
Generic function identity<T>(x: T) -> T.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    #template(T: type)
    fun identity(x: T) -> T {
        return x;
    }
    initial {
        let result: i32 = identity<i32>(42);
        println(cast<string>(result));
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
