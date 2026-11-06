# IncrementalGenInstSwap
## Description
Batch 4.2 fast-lane sentinel for specialization reconciliation: the v1 body requests identity<i64>; the edited body swaps to identity<string> (body-only edit => fast lane). The dead identity<i64> specialization must be evicted and the new one bound, producing exactly the full compile of the edited text.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace app {
#template(T: type)
fun identity(v: T) -> T {
    return v;
}

fun describe(n: i64) -> string {
    return "num=" + cast<string>(n);
}

initial {
    println(describe(identity<i64>(41)));
}
}
```
## Compile
Args: `--disable-coroutine --incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/geninst_v2.penguin`
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
