# IncrementalBodyEdit
## Description
Batch 4.2 fast-lane sentinel: a didChange that only edits a function BODY (the AST-signature fingerprint is unchanged) must recompile through the IncrementalSession's FAST lane and produce exactly the full-compile behavior of the edited text. The first compile runs the v1 program; `--edit` then replaces the whole file with Tests/IncrementalTest/body_v2.penguin (describe now doubles and prefixes differently). The run output therefore proves the recompiled body took effect through the incremental path. --disable-coroutine keeps the session fast-lane-eligible (no scheduler preload => no suspension).

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace app {
fun describe(n: i64) -> string {
    return "num=" + cast<string>(n);
}

initial {
    println(describe(41));
}
}
```
## Compile
Args: `--disable-coroutine --incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/body_v2.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedStdout: EQUALS `value:82
`
ExpectedExitCode: 0
