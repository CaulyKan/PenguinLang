# WhileExpression
## Description
While loop used as expression with break value.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let x: i64 = while (true) { break 1; };
        println(cast<string>(x));
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
