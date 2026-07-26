# MetaCliDefineNoValue
## Description
Phase 5a: `--define FEATURE` (no `=value`) defines the option with the default value "1", which is enough for `#defined("FEATURE")` to be true.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#if (#defined("FEATURE")) {
    fun status() -> string { return "on"; }
} #else {
    fun status() -> string { return "off"; }
}
initial {
    println(status());
}
```

## Compile
Args: `--define FEATURE`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `on
`
ExpectedStderr: DISCARD
