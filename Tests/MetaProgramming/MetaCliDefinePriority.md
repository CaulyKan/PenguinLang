# MetaCliDefinePriority
## Description
Phase 5a: a code-level `#define("MODE","code")` takes priority over a command-line `--define MODE=cli` (code defines run during binding and overwrite the CLI seed). The `#if` condition reads the option value with `#option("MODE")` and string-compares it, so the winner is observable. (Verified: code wins → "code".)

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("MODE","code");
#if (#option("MODE") == "code") {
    fun winner() -> string { return "code"; }
} #else {
    fun winner() -> string { return "cli"; }
}
initial {
    println(winner());
}
```

## Compile
Args: `--define MODE=cli`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `code
`
ExpectedStderr: DISCARD
