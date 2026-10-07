# MtGcHardCapOom
## Description
M5 v0 pressure valve (design doc §10.1), the OOM half: a keep-live cons chain grows the reachable set past the tiny 256KB cap. Every boundary collects and frees nothing (all live), so the smallest pending charge can never be admitted — the boundary reports the deterministic OOM and stops the program with exit code 62 via the exit() channel, instead of cycling parked boundaries forever. The OOM predicate is live + MIN pending charge > cap (not live > cap alone: the parkers stall in the gap between "live" and "live + charge" — a keep-live loop measured parking forever at exactly cap - 48 before that rule existed). Diagnostic goes to stderr (DISCARDed — it embeds live byte counts); stdout sees only "go". Verified single-threaded; the N=4 form behaves identically (same stop channel).

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
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_GC_HARD_CAP=262144`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD
