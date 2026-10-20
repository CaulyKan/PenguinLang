# HandoffImmAccepted
## Description

Two-tier heap P1 checker, positive twin of HandoffNotShareableRejected: under `--strict-handoff` a channel push with a deeply-immutable payload (all fields !mut) compiles, pushes, pops and prints — the Imm tier of the three-strategy split (plan §2.1) crosses threads legally.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class ImmPoint {
    impl IReferenceType;
    x: i64 = 0;
    y: i64 = 0;
}

initial {
    let ch: mut __builtin.Fifo<ImmPoint> =
        new __builtin.Fifo<ImmPoint>(4, new __builtin.FifoPolicy.backpressure());
    let p = new ImmPoint();
    let ok: bool = ch.try_write(p);
    println("written=" + cast<string>(ok);
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model --strict-handoff`

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `written=true
`
ExpectedStderr: DISCARD
