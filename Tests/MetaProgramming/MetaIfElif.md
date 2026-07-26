# MetaIfElif
## Description
Phase 5a: `#if` / `#elif` / `#else` chain. `A` is undefined, `B` is defined (by a preceding `#define`), so the `#elif` branch is taken.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("B","1");
#if (#defined("A")) {
    fun pick() -> string { return "a"; }
} #elif (#defined("B")) {
    fun pick() -> string { return "b"; }
} #else {
    fun pick() -> string { return "none"; }
}
initial {
    println(pick());
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
ExpectedStdout: EQUALS `b
`
ExpectedStderr: DISCARD
