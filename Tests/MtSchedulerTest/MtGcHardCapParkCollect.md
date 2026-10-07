# MtGcHardCapParkCollect
## Description
M5 v0 pressure valve (design doc §10.1): a delta can be arbitrarily long while collection only happens at its boundary, so the allocator carries a hard cap (EMPEROR_GC_HARD_CAP) and a coroutine whose next charge would cross it parks on "wait for GC" — a CO_COND park that is NOT runnable. Four parallel churn coroutines each allocate far past the tiny 256KB cap; the valve parks them, the all-blocked delta converges into the boundary's stop-the-world collection, the freed garbage releases the parkers and the loop makes progress — no deadlock, no OOM. Runs under EMPEROR_THREADS=4 so the parks happen inside the parallel window (workers park mid-delta); the checksum keeps stdout deterministic. With the valve disabled this same shape is just an allocation storm.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Node {
    let v : mut i64;
    fun new(mut this, v : i64) {
        this.v = v;
    }
}

fun churn(id : i64, n : i64) -> i64 {
    let acc : mut i64 = 0;
    let mut i = 0;
    while (i < n) {
        let x : Node = new Node(i);
        acc = acc + x.v % 7 + id;
        i = i + 1;
    }
    return acc;
}

initial {
    let a = async churn(1, 50000);
    let b = async churn(2, 50000);
    let c = async churn(3, 50000);
    let d = async churn(4, 50000);
    let ra : i64 = wait a;
    let rb : i64 = wait b;
    let rc : i64 = wait c;
    let rd : i64 = wait d;
    println("sum " + cast<string>(ra + rb + rc + rd));
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
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum 1099988
`
ExpectedStderr: DISCARD
