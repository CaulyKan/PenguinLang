# MetaCliDefinePriority
## Description
A code-level `#define("MODE","code")` takes priority over a command-line `--define MODE=cli` (code defines run during binding and overwrite the CLI seed — a seed overwrite is not a redefinition). The `#if` condition reads the value with a BARE define name (`MODE == "code"`, redesign §3 — the retired `#option("MODE")` form migrated in batch 2.3), so the winner is observable. (Verified: code wins → "code".)

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("MODE","code");
#if (MODE == "code") {
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
