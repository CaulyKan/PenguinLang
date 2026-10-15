# MtGcHardCapOom
## Description
M5 v0 pressure valve (design doc §10.1), the OOM half: a keep-live cons chain grows the reachable set past the tiny 256KB cap. Every boundary collects and frees nothing (all live), so the smallest pending charge can never be admitted — the boundary reports the deterministic OOM and stops the program with exit code 62 via the exit() channel, instead of cycling parked boundaries forever. The OOM predicate is live + MIN pending charge > cap (not live > cap alone: the parkers stall in the gap between "live" and "live + charge" — a keep-live loop measured parking forever at exactly cap - 48 before that rule existed). Diagnostic goes to stderr (DISCARDed — it embeds live byte counts); stdout sees only "go". Verified single-threaded; the N=4 form behaves identically (same stop channel).


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
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run 1
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD
