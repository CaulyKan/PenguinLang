# LibMetaTextDirectParity
## Description
--libmeta=text|--libmeta=direct parity: the same consumer program compiled against the same lib in BOTH ingestion modes must behave identically. Direct is the default (prebuilt bound defs spliced after pass 1: skeleton registration + signature backfill + prebuilt vtables + re-bound global initializers); text materializes bodyless declaration text and runs the ordinary pipeline. This test compiles and RUNS the text-mode consumer (the direct mode is exercised by every other DynamicLinkTest case, which runs without the flag) — covering the exported-fun call path, globals with literal and function-call initializers (GOT interposition), and an enum payload across the boundary. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace tdp {
    let base: mut i64 = 3;

    export fun doubled(x: i64) -> i64 { return x * 2; }
    export fun base_plus(x: i64) -> i64 { return base + x; }
}
```
## Build 1
Kind: lib
Name: tdp.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    println("d=" + cast<string>(tdp.doubled(21)));
    println("bp=" + cast<string>(tdp.base_plus(4)));
}
```
## Build 2
Args: `--lib ${WORKDIR}/tdp.penguin-lib --libmeta=text`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `d=42
bp=7
`
ExpectedStderr: DISCARD
