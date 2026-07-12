# BasicCodeBlockExpression
## Description
Code block expression returning the last expression value.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let x: i64 = {
            let a: i64 = 1;
            let b: i64 = 2;
            a + b
        };
        println(x);
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
ExpectedStdout: EQUALS `3
`
ExpectedStderr: DISCARD
