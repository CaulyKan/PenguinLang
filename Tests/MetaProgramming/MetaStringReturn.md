# MetaStringReturn
## Description
Phase 6 v2 (Phase 5): a `#fun -> string` returns a compile-time string. The caller-stub stores the returned string pointer into the host global `active_string_result` (returning i64 0); the host splices it as a string literal. `#greet(7)` returns `"hi #7"`. Exercises the non-i64 return channel (Round 1 couldn't return strings — i64-only trampolines). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun greet(n: i64) -> string {
    return "hi #" + cast<string>(n);
}
initial {
    println(#greet(7));
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
ExpectedStdout: EQUALS `hi #7
`
ExpectedStderr: DISCARD
