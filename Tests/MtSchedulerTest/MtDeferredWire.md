# MtDeferredWire
## Description
LatestChannel (the wire/port base) is deferred under the mt scheduler: same-delta writes COLLAPSE to the final value (current() sees the settling value immediately — it is the diagnostic read, not a transaction), while `wait wire` delivers the collapsed value only at the delta boundary — next delta, same tick. Byte-identical under EMPEROR_THREADS=1 and 4; together with Signal/Fifo/Event the whole deferred family is now in place (design doc §8.2 table's wire row).


Multi-run net (migrated from the CI MT-scheduler regression step): three re-runs at EMPEROR_SHUFFLE_DELTAS=1/7/42 with byte-identical output.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
let w : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();

initial {
    w.write(1);
    w.write(2);
    println("read-same-delta " + cast<string>(w.current().some));
    let v : i64 = wait w;
    println("took " + cast<string>(v) + " @" + cast<string>(__builtin._sim_delta()));
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
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `read-same-delta 2
took 2 @2
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `read-same-delta 2
took 2 @2
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `read-same-delta 2
took 2 @2
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `read-same-delta 2
took 2 @2
`
ExpectedStderr: DISCARD
