# MtSpawnFutureExit
## Description
Smoke: async spawn + `wait f` (the parent parks on the result future, woken when the child finishes — the loop kicks future waiters on status 0) and the new exit rule (every coroutine finished → exit 0, no quiescence fingerprint). BabyPenguin's aligned C# scheduler implements the same exit propagation — dual-pinned.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("hello");
}

initial {
    let f = async greet();
    let r : string = wait f;
    println(r);
}

fun greet() -> string {
    return "from-async";
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `hello
from-async
`
ExpectedStderr: DISCARD
