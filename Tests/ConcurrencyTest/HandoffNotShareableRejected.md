# HandoffNotShareableRejected
## Description

Two-tier heap P1 checker, negative case (plan §3/P1): `--strict-handoff` rejects a channel push whose payload type is not Shareable — a mutable reference class (no ICopy, not deeply immutable, no ISync) crossing threads through `Fifo.push()` is `E_HANDOFF_NOT_SHAREABLE`. The Imm payload twin (HandoffImmAccepted) proves the same shape compiles and runs when the payload IS shareable, so this rejection is about the type, not the call.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class MutableState {
    impl IReferenceType;
    n: mut i64 = 0;
}

initial {
    let ch: mut __builtin.Fifo<MutableState> =
        new __builtin.Fifo<MutableState>(4, new __builtin.FifoPolicy.backpressure());
    let s: mut MutableState = new MutableState();
    ch.try_write(s);
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model --strict-handoff`
ExpectedExitCode: 1
