# MtOwnershipCrossSpawn
## Description
The Ownership lattice entry in action: `consumer(o: mut Ownership<mut Cell>)` takes a MUTABLE REFERENCE payload, yet Pass 10 judges the spawn STEALABLE — Own(T) is Shareable for ANY payload because the construction check guarantees the wrapped graph has no aliases. The stolen coroutine move()s the Cell out on its (possibly different) thread and mutates it — sound because it is the sole holder. Byte-identical under EMPEROR_THREADS=1 and 4.


Multi-run net (migrated from the CI MT-scheduler regression step): three re-runs at EMPEROR_SHUFFLE_DELTAS=1/7/42 with byte-identical output.
## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Cell { v: mut i64; }

fun consumer(o: mut __builtin.Ownership<mut Cell>) -> i64 {
    let c : mut Cell = o.move();
    c.v = c.v + 1;
    return c.v;
}

initial {
    let f = async consumer(new __builtin.Ownership<mut Cell>(new Cell()));
    let r : i64 = wait f;
    println("crossed " + cast<string>(r));
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
ExpectedStdout: EQUALS `crossed 1
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `crossed 1
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `crossed 1
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `crossed 1
`
ExpectedStderr: DISCARD
