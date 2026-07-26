# MetaWhileInBody
## Description
Phase 5a.3: statement-level `#while (false)` inside a function body drops the body statement. The first `return` is never emitted so the function returns 2.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun test() -> i64 {
    #while (false) {
        return 1;
    }
    return 2;
}
initial {
    println(cast<string>(test()));
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
ExpectedStdout: EQUALS `2
`
ExpectedStderr: DISCARD
