# ResultTest
## Description
Result<T,E> built-in: ok and error variants with is_ok, is_error, value_or, and error access.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : Result<u32,string> = new Result<u32,string>.ok(10);
        println(cast<string>(a.is_ok()));
        println(cast<string>(a.is_error()));
        println(cast<string>(a.value_or(9)));

        let b : Result<u32,string> = new Result<u32,string>.error("err");
        println(b.error);
        println(cast<string>(b.is_ok()));
        println(cast<string>(b.is_error()));
        println(cast<string>(b.value_or(9)));
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
ExpectedStdout: EQUALS `true
false
10
err
false
true
9
`
ExpectedStderr: DISCARD
