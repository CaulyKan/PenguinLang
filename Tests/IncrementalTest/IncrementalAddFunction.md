# IncrementalAddFunction
## Description
Batch 4.2 slow-lane sentinel: adding a top-level FUNCTION changes the AST-signature fingerprint, so the didChange falls back to the SLOW lane (parse cache + fresh pipeline) — which must still produce exactly the full-compile behavior of the edited text (the extra wrapper function takes effect). Default flags (scheduler preloaded, session slow-lane anyway).

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
Args: `--incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/addfn_v2.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedStdout: EQUALS `[num=41]
`
ExpectedExitCode: 0
