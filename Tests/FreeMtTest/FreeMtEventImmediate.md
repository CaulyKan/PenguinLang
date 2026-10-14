# FreeMtEventImmediate
## Description
In the free form the component family is IMMEDIATE: Event.emit delivers and kicks the parked waiter INLINE (no deferred commit, no boundary round-trip) — a waiter parked on `wait ev` wakes in the SAME convergence round. One emitter, two subscribers, the shape repeated 30x with a fresh Event each round: every iteration contributes (10+20+1)+(10+20+2) = 63, so the total is deterministic (1890) regardless of placement. The emitter uses `wait 0;` (not a pure yield) before emitting so the subscriptions exist before the first delivery — that is the M0 round semantics, unchanged by the free form. Runs under EMPEROR_THREADS=4: the waiters hold an ISync Event and are Pass-10 STEALABLE, so subscribe/unsubscribe/emit genuinely run on different threads — this pins the cross-thread subscriber-list protocol. Bug this pins: Event.unsubscribe used to mutate the subscriber list WITHOUT the component lock (scheduler_free.penguin diverged from scheduler_mt.penguin when the free policy was derived) — a waiter's poll-time unsubscribe racing another waiter's poll or a subscribe could lose BOTH a removal and a push, so the next emit delivered to the stale subscription while the fresh one never registered: the waiter's re-poll never saw the value and the program ended as the lost-wake idle hang (exit 63, "[idle-dump] ... NOWHERE"). Pre-fix that fired ~15% of runs at N=4; the 30-iteration loop amplifies the window (pre-fix ~always red, post-fix 100/100).

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
    let total : mut i64 = 0;
    let it : mut i64 = 0;
    while (it < 30) {
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
        total = total + r1 + r2;
        it = it + 1;
    }
    println("got " + cast<string>(total));
    exit(0);
}
```

## Compile
Args: `--enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `got 1890
`
ExpectedStderr: DISCARD
