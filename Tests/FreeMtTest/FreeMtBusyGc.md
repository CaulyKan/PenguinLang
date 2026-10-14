# FreeMtBusyGc
## Description
free-mt GC under a PERPETUAL delta (the design doc §10's busy-service shape): two churn coroutines allocate strings through an AsyncQueue round-trip without ever letting the system quiesce (the round never converges while work is in flight), while a third runs a tight NO-CALL compute loop — before the loop-back-edge safepoints it could not even reach a poll. The poll-side STW escalation (EMPEROR_GC_STW_MS) parks the polling coroutines onto the valve list once a goal-raised want_collect outlives the threshold, the forced convergence lets the boundary collect at goal-sized heaps, and the loop back-edge poll brings the spinner to the same table. With a 4 MiB min-heap the run does dozens of collections (verified with GC_STATS in dev: fulls>0, peak RSS ~tens of MiB — without the escalation the heap climbs to the hard cap before the valve ever fires). The sums are order-independent so stdout is deterministic.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Box { v : mut i64; }

fun spin(n : i64) -> i64 {
    let keep : mut Box = new Box();
    let acc : mut i64 = 0;
    let i : mut i64 = 0;
    while (i < n) {
        acc = acc + i % 7;
        i = i + 1;
    }
    return acc + keep.v;
}

async fun churn(id : i64) -> i64 {
    let sum : mut i64 = 0;
    let i : mut i64 = 0;
    while (i < 400000) {
        let s : string = "str-" + cast<string>(i % 1000) + "-padding-padding";
        sum = sum + string_length(s) % 13;
        i = i + 1;
    }
    return sum % 1000007;
}

initial {
    let a = async churn(1);
    let b = async churn(2);
    let s = async spin(8000000);
    let ra : i64 = wait a;
    let rb : i64 = wait b;
    let rs : i64 = wait s;
    println("done " + cast<string>(ra) + " " + cast<string>(rb) + " " + cast<string>(rs));
    exit(0);
}
```

## Compile
Args: `--enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_GC_MIN_HEAP=4194304 EMPEROR_GC_GOAL_FACTOR=2 EMPEROR_GC_YOUNG=2097152`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 955979 955979 23999997
`
ExpectedStderr: DISCARD
