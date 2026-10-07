# WaitConditionNeverTrue
## Description
The never-satisfied condition migrated to a blocked channel wait (`wait <condition>` is REMOVED — concurrency redesign M0). A consumer blocked forever on an empty channel with no producers is quiescence, not a hang: the program ends normally with exit code 0 — waiting is a legal final state, not an error.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(1, new __builtin.FifoPolicy.backpressure());

initial {
    println("start");
    let v : i64 = wait q;
    println("never");
}

initial {
    wait 5;
    exit(0);
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `start
`
ExpectedStderr: DISCARD
