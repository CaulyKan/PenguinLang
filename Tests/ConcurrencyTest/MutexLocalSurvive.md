# MutexLocalSurvive
## Description

Two-tier heap M5 acceptance (plan §4 M5 ①/②): objects swapped into a Mutex inside with() are promoted at crit-exit (still inside the lock), and must survive the domain-local collections that the follow-up churn fires (EMPEROR_GC_LOCAL=1 — a local cycle on every span refill). Locks in: the payload mutation through the `mut T` alias, the freshly allocated graph's closure promotion, the mixed-span slow path (the promoted Counter's span carries nshared>0 while the churn's dead slots recycle around it), and a final explicit gc_collect agreeing with the local cycles. BabyPenguin ignores the env (no local collector there) and produces the same output.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Counter {
    impl IReferenceType;
    n: mut i64 = 0;
}

initial {
    let c: mut Counter = new Counter();
    let m: mut __builtin.Mutex<Counter> = new __builtin.Mutex<Counter>(c);

    // swap-in inside the critical section: fresh is crit-tracked and
    // promoted at with() exit; the payload alias mutation writes through
    m.with(fun(v: mut Counter) -> void {
        let fresh: mut Counter = new Counter();
        fresh.n = 99;
        v.n = fresh.n;
    });

    // same-size-class churn: ~73 slots per span at 112B -> thousands of
    // refills -> local collections fire around the promoted object's span
    let i: mut i64 = 0;
    while (i < 5000) {
        let tmp = new Counter();
        tmp.n = i;
        i = i + 1;
    }

    // (a final explicit global collect is EP-only machinery — the local
    // cycles above are the point of this case; BabyPenguin mirrors the
    // output without a local collector)
    println("survive=" + cast<string>(m.get().n));
}
```

## Run
Args: ``
Env: `EMPEROR_GC_LOCAL=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `survive=99
`
ExpectedStderr: DISCARD
