# CodeBlockAsExpressionStatement
## Description
Code block used as expression statement (discarding result).

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let a: i64 = 1;
        {
            let b: i64 = a + 1;
        };
        println(cast<string>(a));
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
ExpectedStdout: EQUALS `1
`
ExpectedStderr: DISCARD
