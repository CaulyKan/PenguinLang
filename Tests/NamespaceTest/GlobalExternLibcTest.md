# GlobalExternLibcTest
## Description
A bare top-level `extern` maps to its own literal C symbol: `extern fun abs(v: i32) -> i32;` links against real libc `abs`. Top-level extern declarations are exempt from the per-file anonymous namespace (`_ns_<file>_<hash>`) — a C symbol is global by nature, and wrapping it would produce an unstable `_ns_..._abs` no C side can implement. Namespaced externs map by the same universal rule to `<ns>_<name>` (see NamespacedExternMappingTest). BabyPenguin's VM has no C linkage for user externs, hence Pass2/Pass3 only.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
extern fun abs(v: i32) -> i32;

initial {
    println("abs(-42)=" + cast<string>(abs(-42)));
    println("abs(7)=" + cast<string>(abs(7)));
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
ExpectedStdout: EQUALS `abs(-42)=42
abs(7)=7
`
ExpectedStderr: DISCARD
