# GcNestedFinalizerCollect
## Description
Finalizer-phase reentrancy guard: a collection's finalizer phase runs PENGUIN
`dispose_mem` code (Vector releasing its raw buffer), and that finalizer's own
safepoint polls must not start another collection mid-flight. The collector
holds `_emperor_gc_collecting` across its mark/sweep/finalize phases; a
re-entrant dispose poll finalizing LIVE objects in place was the historical
failure shape (raw buffer freed and zeroed mid-use — the EmperorPenguinLib
self-compile crashed at ~83s in `List.at` exactly this way; this minimal
program is the end-to-end GREEN GUARD, not a red repro).
`EMPEROR_GC_STRESS_EVERY=8` makes every 8th poll a full collect — stress
collects fire inside finalizer bodies while a long-lived container (held from
the initial frame across every collection) must come out intact. Green = no
collection-inside-a-collection.
Native-only (EmperorPenguin runtime machinery; BabyPenguin ignores the env
vars).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let keep: mut std.Vector<i64> = new std.Vector<i64>();
    keep.push(11);
    keep.push(22);
    let i: mut i64 = 0;
    while (i < 4000) {
        let junk: mut std.Vector<i64> = new std.Vector<i64>();
        junk.push(i);
        junk.push(i + 1);
        i = i + 1;
    }
    if (keep.size() != 2) {
        println("CORRUPT size");
    } else {
        if (keep.at(0).some != 11) {
            println("CORRUPT v0");
        }
        if (keep.at(1).some != 22) {
            println("CORRUPT v1");
        }
    }
    println("done " + cast<string>(keep.size()) + " " + cast<string>(keep.at(1).some));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: `EMPEROR_GC_STRESS_EVERY=8`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `done 2 22
`
ExpectedStderr: DISCARD
