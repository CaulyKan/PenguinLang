# FreeMtAsyncQueue
## Description
AsyncQueue<T> is the free form's cross-thread pipe (design doc §8: free-mt uses "immediate + locked" components): an MPMC queue guarded by an AtomicBool spinlock (a failed test-and-set backs off with a pure yield — never holding the lock across a park); takers finding it empty park as condition waiters and are woken by enqueue. ISync — coroutines holding one are Pass-10 STEALABLE, so under EMPEROR_THREADS=4 producer and consumers genuinely run on different threads. Two consumers drain 200 items (each item counted once — `taken` totals exactly the produced sum) and a sentinel per consumer ends them; the program then finishes and exits (decision E needs no explicit exit here — every coroutine terminates).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun consumer(q: mut AsyncQueue<i64>, id: i64) -> i64 {
    let sum : mut i64 = 0;
    let n : mut i64 = 0;
    while (true) {
        let v : i64 = q.take();
        if (v < 0) {
            return sum;
        }
        sum = sum + v;
        n = n + 1;
    }
    return sum;
}

initial {
    let q : mut AsyncQueue<i64> = new AsyncQueue<i64>();
    let c1 = async consumer(q, 1);
    let c2 = async consumer(q, 2);
    let i : mut i64 = 0;
    while (i < 200) {
        q.enqueue(i);
        i = i + 1;
    }
    q.enqueue(-1);
    q.enqueue(-1);
    let s1 : i64 = wait c1;
    let s2 : i64 = wait c2;
    println("total " + cast<string>(s1 + s2) + " items 19900 wanted");
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
ExpectedStdout: EQUALS `total 19900 items 19900 wanted
`
ExpectedStderr: DISCARD
