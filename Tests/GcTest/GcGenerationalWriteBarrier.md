# GcGenerationalWriteBarrier
## Description
Generational GC (GC v2 phase 3b) write-barrier end-to-end guard. `EMPEROR_GC_MODE=precise` turns on the nursery; an explicit `gc_collect()` promotes the Holder into the old generation (first-survival promotion), then every loop iteration stores a FRESH young string into the OLD object's field — exactly the old→young edge the emitter's `_emperor_gc_write_barrier` records into the remembered set. `EMPEROR_GC_NO_RS_FALLBACK=1` disables the whole-old-generation scan fallback, so a missing/incorrect barrier leaves the field pointing at a recycled nursery address and the output corrupts (or crashes). Each iteration immediately collects again (a minor that must rewrite `h.s` to the promoted copy). Green = barrier emission, remembered set, promotion, and slot rewriting all correct. Known-good on BabyPenguin's semantics; native-only because the barrier is EmperorPenguin runtime machinery (BabyPenguin ignores the env vars and passes trivially — that combination is not in Apply To).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Holder {
    s: string;
    n: i64;
}

fun churn(round: i64) -> i64 {
    let acc: mut i64 = 0;
    let i: mut i64 = 0;
    while (i < 300) {
        let tmp: string = "g" + cast<string>(round) + "-" + cast<string>(i);
        acc = acc + string_length(tmp);
        i = i + 1;
    }
    return acc;
}

initial {
    let h: mut Holder = new Holder();
    h.s = "old";
    h.n = 1;
    gc_collect();
    let i: mut i64 = 0;
    let acc: mut i64 = 0;
    while (i < 60) {
        h.s = "young-" + cast<string>(i);
        h.n = h.n + 1;
        gc_collect();
        if (string_length(h.s) == 0) {
            println("CORRUPT");
        }
        acc = acc + churn(i);
        i = i + 1;
    }
    println(h.s);
    println("n=" + cast<string>(h.n));
    println("acc=" + cast<string>(acc));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_GC_MODE=precise EMPEROR_GC_NO_RS_FALLBACK=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `young-59
n=61
acc=116400
`
ExpectedStderr: DISCARD
