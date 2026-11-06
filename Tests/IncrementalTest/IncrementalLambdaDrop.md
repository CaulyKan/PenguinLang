# IncrementalLambdaDrop
## Description
Batch 4.2 fast-lane sentinel for synthesized-def eviction: the v1 body binds a LAMBDA (the session synthesizes a closure class with a per-file counter); the edited text DROPS the lambda (body-only edit, fingerprint unchanged => fast lane). The fast lane must evict the changed file's synthesized closure class and reset its per-file counter, producing exactly the full compile of the edited text.

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
    let wrap: fun<string, string> = fun(s: string) -> string {
        return "[" + s + "]";
    };
    println(wrap(describe(41)));
}
}
```
## Compile
Args: `--disable-coroutine --incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/lambdadrop_v2.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedStdout: EQUALS `num=41
`
ExpectedExitCode: 0
