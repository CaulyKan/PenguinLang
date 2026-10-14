# FreeMtPerfAnalyzeReport
## Description
`EP_MULTITHREAD_SCHEDULER_PERF_ANALYZE=1` (env) makes the mt runtime account every scheduler thread's wall time into four phases and print the per-thread report to stderr at program exit (after the workers join): busy (inside `_emperor_co_switch_in`, i.e. running penguin code), idle (parked waiting for work), schedule (queues/wakes/boundary bookkeeping), gc (inside a GC driver). Two pure-compute stealable workers under `EMPEROR_THREADS=2` guarantee both slots did real work: the round-robin distributor hands one spawn to the worker thread, so exactly two thread lines (main + worker 1) plus the total line appear, with every time/percent value timing-dependent (MATCH holes). The stdout sum is fully deterministic.


## Apply To
* EmperorPenguin Pass3

## Test Code
```
fun work(k: i64) -> i64 {
    let acc : mut i64 = 0;
    let i : mut i64 = 0;
    while (i < 2000000) {
        acc = acc + i * k;
        i = i + 1;
    }
    return acc;
}

initial {
    let a = async work(1);
    let b = async work(2);
    let ra : i64 = wait a;
    let rb : i64 = wait b;
    println("sum " + cast<string>(ra + rb));
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
Env: `EP_MULTITHREAD_SCHEDULER_PERF_ANALYZE=1 EMPEROR_THREADS=2`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `sum 5999997000000
`
ExpectedStderr: MATCH `[mt-perf] per-thread time (EP_MULTITHREAD_SCHEDULER_PERF_ANALYZE):
[mt-perf] thread 0 (main): busy \d+us (\d+.\d+%)  idle \d+us (\d+.\d+%)  schedule \d+us (\d+.\d+%)  gc \d+us (\d+.\d+%)
[mt-perf] thread 1: busy \d+us (\d+.\d+%)  idle \d+us (\d+.\d+%)  schedule \d+us (\d+.\d+%)  gc \d+us (\d+.\d+%)
[mt-perf] total:      busy \d+us (\d+.\d+%)  idle \d+us (\d+.\d+%)  schedule \d+us (\d+.\d+%)  gc \d+us (\d+.\d+%)
`
