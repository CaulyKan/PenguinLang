# MtGcHardCapClassicAdvisory
## Description
M5 v0 pressure valve scope: the cap parks allocators only under the MT kernel (_emperor_sched_alloc_park returns 0 without it). The same keep-live shape that exits 62 under the mt scheduler with a 256KB cap (MtGcHardCapOom) runs UNCHANGED under the classic scheduler (--enable-coroutine alone) with the same tiny cap — no parks, no forced collections, the goal cadence bounds the heap exactly as before the valve existed. This locks the "zero classic-scheduler regression" property of the pressure-valve change.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Node {
    let v : mut i64;
    let rest : mut Option<Node>;
    fun new(mut this, v : i64, rest : Option<Node>) {
        this.v = v;
        this.rest = rest;
    }
}

fun grow() {
    let keep : mut Option<Node> = new Option<Node>.none();
    let mut i = 0;
    while (i < 20000) {
        keep = new Option<Node>.some(new Node(i, keep));
        i = i + 1;
    }
    println("grow done");
}

initial {
    async grow();
    println("go");
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run 1
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `go
grow done
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `go
grow done
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `go
grow done
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `go
grow done
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `go
grow done
`
ExpectedStderr: DISCARD
