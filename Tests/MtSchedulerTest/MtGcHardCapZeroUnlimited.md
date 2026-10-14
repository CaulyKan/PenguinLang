# MtGcHardCapZeroUnlimited
## Description
M5 v0 pressure valve knob semantics: EMPEROR_GC_HARD_CAP=0 is EXPLICIT UNLIMITED — the same keep-live program that dies with exit 62 under a 256KB cap (see MtGcHardCapOom) runs to completion untouched when the cap is zeroed (the classic-scheduler / no-mt half of the "cap is advisory where the valve cannot act" parity is MtGcHardCapClassicAdvisory).

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

## Run
Args: ``
Env: `EMPEROR_GC_HARD_CAP=0`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `go
grow done
`
ExpectedStderr: DISCARD
