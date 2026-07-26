# MetaIfInBody
## Description
Phase 5a.3: a `#if` inside a function body resolves at compile time — the taken branch's statements are spliced into the body. Here `#if (true)` keeps `return mode + 1;` ahead of the fallback `return mode;`, so compute(10) returns 11.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun compute(mode: i64) -> i64 {
    #if (true) {
        return mode + 1;
    }
    return mode;
}
initial {
    println(cast<string>(compute(10)));
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
ExpectedStdout: EQUALS `11
`
ExpectedStderr: DISCARD
