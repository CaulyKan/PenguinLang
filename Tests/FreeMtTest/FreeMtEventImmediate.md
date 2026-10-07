# FreeMtEventImmediate
## Description
In the free form the component family is IMMEDIATE: Event.emit delivers and kicks the parked waiter INLINE (no deferred commit, no boundary round-trip) — a waiter parked on `wait ev` wakes in the SAME convergence round. One emitter, two subscribers: both waiters observe both emissions (each `wait ev` is a fresh one-shot subscription). The emitter uses `wait 0;` (not a pure yield) before emitting so the subscriptions exist before the first delivery — that is the M0 round semantics, unchanged by the free form.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun waiter(ev: mut Event<i64>, id: i64) -> i64 {
    let a : i64 = wait ev;
    let b : i64 = wait ev;
    return a + b + id;
}

initial {
    let ev : mut Event<i64> = new Event<i64>();
    let w1 = async waiter(ev, 1);
    let w2 = async waiter(ev, 2);
    // `wait 0;` parks for the REST of the round: the spawned waiters run
    // first and park on `wait ev` (a pure yield would re-queue the emitter
    // ahead of them — the emissions would fire before any subscription
    // exists and be lost).
    wait 0;
    ev.emit(10);
    // One more round: the waiters consume emission #1 and re-subscribe
    // (`wait ev` is a one-shot subscription per wait — a second emission
    // landing before the re-subscribe would find no subscriber).
    wait 0;
    ev.emit(20);
    let r1 : i64 = wait w1;
    let r2 : i64 = wait w2;
    println("got " + cast<string>(r1 + r2));
    exit(0);
}
```

## Compile
Args: `--enable-free-mt`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `got 63
`
ExpectedStderr: DISCARD
