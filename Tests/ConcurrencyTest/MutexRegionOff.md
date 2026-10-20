# MutexRegionOff
## Description

Two-tier heap master-switch regression: with EMPEROR_GC_REGION=0 the write-point promotion machinery is a complete no-op (plan §6 rollback switch) — the same Mutex/channel surface must produce byte-identical output to the region-on run (MutexChannelPromote's expectations). Run with the env set; green means the off path is exact today-single-heap behavior.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Node {
    impl IReferenceType;
    value: mut i64 = 0;
}

initial {
    let g: mut Node = new Node();
    g.value = 5;
    let m: mut __builtin.Mutex<Node> = new __builtin.Mutex<Node>(g);
    m.with(fun(v: mut Node) -> void {
        let fresh: mut Node = new Node();
        fresh.value = v.value + 2;
        v.value = fresh.value;
    });
    println("region-off=" + cast<string>(m.get().value));
}
```

## Run
Args: ``
Env: `EMPEROR_GC_REGION=0`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `region-off=7
`
ExpectedStderr: DISCARD
