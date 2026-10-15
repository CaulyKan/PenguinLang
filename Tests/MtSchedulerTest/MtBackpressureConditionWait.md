# MtBackpressureConditionWait
## Description
Fifo backpressure under the mt scheduler is a CONDITION wait, not a bare-wait poll: a full-channel producer parks WITHOUT being owed a delta, so the clock is not pinned and the 10-tick consumer wakes on time; the producer then resumes in the SAME delta once the consumer's take frees the slot. Under the classic scheduler's bare-wait polling this program also completes but pays one round per retry; under the mt semantics a bare-wait retry loop would pin the tick forever (docs/zh/wip/concurrent_scheduler.md §0.3 — this test is the positive form of that counterexample). BabyPenguin's aligned C# scheduler implements the same condition-park backpressure (`_sched_future_park` note in Fifo.write's loop, woken by the consumer's deferred kick) — dual-pinned.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
let ch : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(1, new __builtin.FifoPolicy.backpressure());

initial {                                   // producer: capacity 1, writes 1/2
    ch.write(1);
    ch.write(2);
    println("P done @" + cast<string>(__builtin._sim_now()));
}

initial {                                   // consumer: sleeps 10 ticks first
    wait 10;
    let v1 : i64 = wait ch;
    let v2 : i64 = wait ch;
    println("C " + cast<string>(v1) + "," + cast<string>(v2) + " @" + cast<string>(__builtin._sim_now()));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run 1
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `P done @10
C 1,2 @10
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `P done @10
C 1,2 @10
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `P done @10
C 1,2 @10
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `P done @10
C 1,2 @10
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `P done @10
C 1,2 @10
`
ExpectedStderr: DISCARD
