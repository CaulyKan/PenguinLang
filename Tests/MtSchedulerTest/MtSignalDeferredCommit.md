# MtSignalDeferredCommit
## Description
Signal<T> — the deferred-commit component (design doc §6.4/§8, M6): write() parks the value, read() returns the last COMMITTED value, the delta boundary commits every pending write in registration order and wakes parked waiters. Three properties asserted: (1) a write is invisible within its own delta (read-after-write sees 10); (2) the boundary commit makes it visible in the next delta (99); (3) `wait sig` — FIRST-OBSERVER semantics: the unobserved 99-commit is delivered immediately, then a parked waiter is woken by the writer's 42 commit at the next boundary (delta 3). Output is identical under EMPEROR_THREADS=1 and =4 — the deferred component is the determinism-subset building block.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let s : mut __builtin.Signal<i64> = new __builtin.Signal<i64>(10);

    s.write(99);
    println("read-after-write " + cast<string>(s.read()));
    wait;
    println("read-next-delta " + cast<string>(s.read()));

    let v1 : i64 = wait s;
    println("observed " + cast<string>(v1));

    async writer(s);
    let v2 : i64 = wait s;
    println("waited " + cast<string>(v2) + " @" + cast<string>(__builtin._sim_delta()));
}

async fun writer(s: mut __builtin.Signal<i64>) {
    s.write(42);
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
ExpectedStdout: EQUALS `read-after-write 10
read-next-delta 99
observed 99
waited 42 @3
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `read-after-write 10
read-next-delta 99
observed 99
waited 42 @3
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `read-after-write 10
read-next-delta 99
observed 99
waited 42 @3
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `read-after-write 10
read-next-delta 99
observed 99
waited 42 @3
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `read-after-write 10
read-next-delta 99
observed 99
waited 42 @3
`
ExpectedStderr: DISCARD
