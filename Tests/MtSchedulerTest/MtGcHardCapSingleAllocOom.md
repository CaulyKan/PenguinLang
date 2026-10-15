# MtGcHardCapSingleAllocOom
## Description
M5 v0 pressure valve (design doc §10.1), the never-fits corner: `"x".repeat(2000000)` charges ~2MB in one allocation against a 64KB cap — no collection can ever admit it (string building doubles through intermediates, so in practice the boundary's min-charge check catches the first intermediate that cannot fit; both OOM paths report exit code 62 the same way). Before the valve this shape was an unbounded allocation storm inside one delta; now it is a clean deterministic stop.


Multi-run net (migrated from the CI MT-scheduler regression step): one re-run at EMPEROR_THREADS=4 and three at EMPEROR_SHUFFLE_DELTAS=1/7/42 (EMPEROR_THREADS=1) — the output must stay byte-identical in every configuration.
## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun big() {
    let s : string = "x".repeat(2000000);
    println(s.length());
}

initial {
    async big();
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
Env: `EMPEROR_GC_HARD_CAP=65536`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD



## Run 2
Args: ``
Env: `EMPEROR_GC_HARD_CAP=65536 EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 3
Args: ``
Env: `EMPEROR_GC_HARD_CAP=65536 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 4
Args: ``
Env: `EMPEROR_GC_HARD_CAP=65536 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD


## Run 5
Args: ``
Env: `EMPEROR_GC_HARD_CAP=65536 EMPEROR_THREADS=1 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 62
ExpectedStdout: EQUALS `go
`
ExpectedStderr: DISCARD
