# SimpleCodeBlockExpression
## Description
Simple code block returning a literal.

## Apply To
* BabyPenguin

## Test Code
```
    initial {
        let x: i64 = { 42 };
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
ExpectedStdout: EQUALS `42
`
ExpectedStderr: DISCARD
