# NestedCodeBlockExpression
## Description
Nested code block expressions.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let x: i64 = {
            let outer: i64 = {
                let inner: i64 = 5;
                inner
            };
            outer + 1
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
ExpectedStdout: EQUALS `6
`
ExpectedStderr: DISCARD
