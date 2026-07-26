# MetaIfElse
## Description
Phase 5a: when the `#if` condition is false, the `#else` branch's definitions are taken. `NOPE` is never defined, so `speed()` resolves to the else body.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#if (#defined("NOPE")) {
    fun speed() -> string { return "fast"; }
} #else {
    fun speed() -> string { return "slow"; }
}
initial {
    println(speed());
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
ExpectedStdout: EQUALS `slow
`
ExpectedStderr: DISCARD
