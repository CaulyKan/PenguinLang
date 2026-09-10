# GarbageCollection
## Description
EmperorPenguin GC tests: retains reachable objects, preserves string locals/globals, frees memory on collection, and reflects allocations. Uses gc_collect/gc_info builtins only available in EmperorPenguin.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    class Node {
        val: i64;
        fun new(mut this, v: i64) {
            this.val = v;
        }
    }
    initial {
        let anchor = new Node(999);
        let i: mut i64 = 0;
        while (i < 1000) {
            let tmp = new Node(i);
            i = i + 1;
        }
        gc_collect();
        println(cast<string>(anchor.val));
    }
}
namespace __c2 {
    initial {
        let s: string = "alive";
        let i: mut i64 = 0;
        while (i < 1000) {
            let tmp: string = "garbage" + cast<string>(i);
            i = i + 1;
        }
        gc_collect();
        println(s);
    }
}
namespace __c3 {
    let msg: string = "global_alive";
    initial {
        let i: mut i64 = 0;
        while (i < 1000) {
            let tmp: string = "noise" + cast<string>(i);
            i = i + 1;
        }
        gc_collect();
        println(msg);
    }
}
namespace __c4 {
    initial {
        // Fresh garbage AFTER the baseline read: ~30KB of dead strings.
        // (Asserting on pre-existing heap deltas is inherently flaky — a
        // conservative collector legitimately retains a few dozen bytes of
        // register/stack residue depending on the process's address layout,
        // which can zero a tiny before/after margin run-to-run. A large
        // fresh batch makes the freed majority dominate any bounded
        // retention, so the assertion is deterministic.)
        let before: i64 = gc_info();
        let i: mut i64 = 0;
        while (i < 1000) {
            let tmp: string = "garbage-churn-" + cast<string>(i);
            i = i + 1;
        }
        gc_collect();
        let after: i64 = gc_info();
        if (after < before + 8000) {
            println("freed");
        } else {
            println("no_free");
        }
    }
}
namespace __c5 {
    initial {
        // Constant-literal concat is FOLDED at bind time into one static
        // .rodata literal (SemanticBindExpressions.fold_string_literal_concats):
        // it must NOT grow the GC heap. A runtime concat (variable operand)
        // still allocates and must grow it — asserting both keeps this test
        // sensitive to the heap behavior on each side of the fold.
        let before: i64 = gc_info();
        let s: string = "hello" + " world";
        let mid: i64 = gc_info();
        let r: string = "value=" + cast<string>(mid);
        let after: i64 = gc_info();
        if (mid == before && after > mid) {
            println("grew");
        } else {
            println("no_grow");
        }
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
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `999
alive
global_alive
freed
grew
`
ExpectedStderr: DISCARD
