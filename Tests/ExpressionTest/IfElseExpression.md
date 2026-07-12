# IfElseExpression
## Description
If-else used as expression.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let x: i64 = if (true) {1} else {2};
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
ExpectedStdout: EQUALS `1
`
ExpectedStderr: DISCARD
