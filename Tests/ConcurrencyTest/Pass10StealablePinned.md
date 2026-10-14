# Pass10StealablePinned
## Description
Pass 10 confinement analysis (mt scheduler): a pure-compute coroutine (no globals, no externs, no channels, value-only result) is judged STEALABLE and its spawn call is rewritten to `_co_spawn_entry_stealable` (verified in the emitted .ll); a coroutine whose callee writes a global is PINNED with the callee named in the diagnostic. The -Wconcurrency diagnostics land on stderr.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
fun compute() -> i64 {
    let acc : mut i64 = 0;
    let i : mut i64 = 0;
    while (i < 10) {
        acc = acc + i * 2;
        i = i + 1;
    }
    return acc;
}

initial {
    let f1 = async compute();
    let r1 : i64 = wait f1;
    println("pure " + cast<string>(r1));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model -Wconcurrency`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `pure 90
`
ExpectedStderr: DISCARD
