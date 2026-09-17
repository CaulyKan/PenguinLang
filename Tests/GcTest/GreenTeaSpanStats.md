# GreenTeaSpanStats
## Description
GC v3 green-tea span-heap introspection end-to-end guard. Runs under
`EMPEROR_GC_MODE=greentea` (the span collector) and asserts the additive
`gc_gt_stats(which)` counters behave: after allocating a wave of
small objects and dropping them, an explicit collect must (a) run exactly
one cycle (which=7 — the ~1MB wave stays far under the unified goal floor
of min_heap + young budget, so no auto
cycle fires), (b) recycle whole spans wholesale (which=5 > 0 — the dead
wave's spans emptied), (c) take the representative fast path (which=6 > 0
— the kept object is its span's only gray slot), (d) report a live set
bounded by the kept object (which=0 < 1MiB), and (e) the span-resident
object survives the collect. Outside greentea mode all counters read 0
(the sweep functions are greentea-driver-only, so the stub never
increments). Pass2/Pass3 only: the extern binds the EmperorPenguin C
runtime.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __gt {
    // The self-referential `next` field makes Node a REFERENCE type: the
    // classifier auto-values any class whose fields are all value-like
    // (primitives AND string), and a value-class Node would live entirely
    // in stack allocas and never touch the heap.
    class Node {
        val: i64;
        next: Node;
        fun new(mut this, v: i64) {
            this.val = v;
        }
    }
    // The churn runs in a HELPER whose frame dies before the collect:
    // the main-stack conservative cover scans [sp, stack_bottom), so a
    // helper's dead frame (below sp at collect time) cannot retain its
    // stale junk pointers — the junk spans empty and recycle wholesale.
    // Churn in main's own frame would leave one stale pointer per span
    // and keep every span partially alive (the cover is conservative:
    // retention, never premature free).
    fun churn(n: i64) {
        let i: mut i64 = 0;
        while (i < n) {
            let junk = new Node(i);
            i = i + 1;
        }
    }
    initial {
        let cycles0: i64 = gc_gt_stats(7);
        let keep: mut Node = new Node(424242);
        churn(30000);
        gc_collect();
        let cycles1: i64 = gc_gt_stats(7);
        let wholesale: i64 = gc_gt_stats(5);
        let rep: i64 = gc_gt_stats(6);
        let live: i64 = gc_gt_stats(0);
        println("cycles " + cast<string>(cycles1 - cycles0));
        println("wholesale>0 " + cast<string>(wholesale > 0));
        println("rep>0 " + cast<string>(rep > 0));
        println("keep " + cast<string>(keep.val));
        println("live_small " + cast<string>(live < 1048576));
    }
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
Env: `EMPEROR_GC_MODE=greentea`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `cycles 1
wholesale>0 true
rep>0 true
keep 424242
live_small true
`
ExpectedStderr: DISCARD
