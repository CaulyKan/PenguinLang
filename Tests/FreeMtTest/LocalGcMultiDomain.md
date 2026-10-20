# LocalGcMultiDomain
## Description

Two-tier heap M4b acceptance (plan §4 M4): domain-local collections running inside concurrent workers must not corrupt anything and must not stall the other domains — the local collector is fired by the OWN thread on its allocation slow path (a safepoint by construction; intra-domain coroutines are cooperatively scheduled, so the whole domain is quiescent), and cross-domain safety comes from the span domain binding + Inv-S5 + the global/local mutual exclusion. Four stealable churn workers (each bound to a different domain under EMPEROR_THREADS=4) allocate thousands of dead objects (firing local cycles via EMPEROR_GC_LOCAL=8) while mutating Mutex-held payloads; the main coroutine collects and prints. Byte-identical output across thread counts is the determinism net; the single printed line proves the payloads survived every interleaving of local collections and lock traffic. Pass3-only (needs the mt runtime with real threads).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Counter {
    impl IReferenceType;
    n: mut i64 = 0;
}

fun churner(m: mut __builtin.Mutex<Counter>, rounds: i64) -> i64 {
    let i: mut i64 = 0;
    while (i < rounds) {
        let junk = new Counter();
        junk.n = i;
        m.with(fun(v: mut Counter) -> void { v.n = v.n + 1; });
        i = i + 1;
    }
    return m.get().n;
}

initial {
    let m0: mut __builtin.Mutex<Counter> = new __builtin.Mutex<Counter>(new Counter());
    let m1: mut __builtin.Mutex<Counter> = new __builtin.Mutex<Counter>(new Counter());
    let m2: mut __builtin.Mutex<Counter> = new __builtin.Mutex<Counter>(new Counter());
    let m3: mut __builtin.Mutex<Counter> = new __builtin.Mutex<Counter>(new Counter());
    let f0 = async churner(m0, 500);
    let f1 = async churner(m1, 500);
    let f2 = async churner(m2, 500);
    let f3 = async churner(m3, 500);
    let r0: i64 = wait f0;
    let r1: i64 = wait f1;
    let r2: i64 = wait f2;
    let r3: i64 = wait f3;
    println("sum=" + cast<string>(r0 + r1 + r2 + r3));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model --strict-handoff`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run 1
Args: ``
Env: `EMPEROR_THREADS=4 EMPEROR_GC_LOCAL=8`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum=2000
`
ExpectedStderr: DISCARD

## Run 2
Args: ``
Env: `EMPEROR_THREADS=1 EMPEROR_GC_LOCAL=8`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum=2000
`
ExpectedStderr: DISCARD
