# GcGenerationalWriteBarrier
## Description
Store-into-live-object survival guard (historically the v2 write-barrier end-to-end test; the barrier machinery itself is gone — the collector is single-generation). An explicit `gc_collect()` marks the Holder live, then every loop iteration replaces its field with a FRESH string and collects again immediately: a cycle that misjudges the just-stored target (mark order, quarantine cover, sweep phasing) reads a recycled slot and prints CORRUPT or a wrong final value. The interleaved `churn` allocates ~300 short strings between iterations to keep the heap churning across each collect. Green = field stores into live objects survive immediate collections byte-exact. Native-only because `gc_collect()` is EmperorPenguin runtime machinery (BabyPenguin has no C runtime — that combination is not in Apply To).

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
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `young-59
n=61
acc=116400
`
ExpectedStderr: DISCARD
