# MtEventManyWaiters
## Description
The multi-waiter directed-wake shape on the deferred Event: FOUR stealable waiters (Event is ISync — at EMPEROR_THREADS=4 they genuinely distribute across threads), each waiting THREE times; every `wait ev` is a fresh one-shot subscription, so each waiter independently observes EVERY emission through its own value slot — the total (61+62+63+64 = 250) is deterministic despite cross-thread placement. The emitter paces with DOUBLE `wait 0` between emissions — the sound idiom under design doc §7.1 (same-delta cross-coroutine order is unspecified): the boundary kick lands the waiters in the SAME delta as the emitter's owed requeue, and at N>1 the emitter's next emit would race the waiters' RE-subscription (the next wait's subscription is created only when the waiter's coroutine gets scheduled); one settling round guarantees every waiter parked on a fresh subscription before the next emit fires.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun waiter(ev: mut __builtin.Event<i64>, id: i64) -> i64 {
    let a : i64 = wait ev;
    let b : i64 = wait ev;
    let c : i64 = wait ev;
    return a + b + c + id;
}

initial {
    let ev : mut __builtin.Event<i64> = new __builtin.Event<i64>();
    let w1 = async waiter(ev, 1);
    let w2 = async waiter(ev, 2);
    let w3 = async waiter(ev, 3);
    let w4 = async waiter(ev, 4);
    wait 0;
    ev.emit(10);
    wait 0;
    wait 0;
    ev.emit(20);
    wait 0;
    wait 0;
    ev.emit(30);
    let s1 : i64 = wait w1;
    let s2 : i64 = wait w2;
    let s3 : i64 = wait w3;
    let s4 : i64 = wait w4;
    println("all " + cast<string>(s1 + s2 + s3 + s4));
    exit(0);
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
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `all 250
`
ExpectedStderr: DISCARD
