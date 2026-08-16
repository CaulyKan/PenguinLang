# StdArrayAutoDispose
## Description
GC auto-finalization of `std.Array<T,N>` (EmperorPenguin/std/penguin/array.penguin): Array implements `__builtin.IMemoryDispose`, so the collector calls `dispose_mem()` when a dead Array shell is swept — releasing the out-of-GC-arena `_malloc` buffer that the collector cannot see. The garbage arrays are created in a helper function and inside a loop, exercising the function-context specialization path (see ArraySetCallInFunction). Then asserts `gc_info()` drops across `gc_collect()` (shells collected; their raw buffers finalized; a single stale-pointer retention is expected under conservative marking). A reachable Array must survive with intact data, and a manual double `dispose_mem()` must be idempotent (buf!=0 guard) — the same property the GC relies on when an owner (e.g. HashMap) disposes an inner Vector before its own finalizer runs. Pass3-only (array.penguin is bootstrap-deferred stdlib passed via Compile.Args; pointer IR is EmperorPenguin-native).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    fun make_array_garbage() {
        let i: mut i64 = 0;
        while (i < 50) {
            let a = new std.Array<i64, 100>();
            i = i + 1;
        }
    }

    initial {
        make_array_garbage();
        let before: i64 = gc_info();
        gc_collect();
        let after: i64 = gc_info();
        if (after < before) {
            println("collected");
        } else {
            println("no_collect");
        }
        let mut keep = new std.Array<i64, 4>();
        keep.set(2, 42);
        println("keep=" + cast<string>(keep.at(2).some));
        keep.dispose_mem();
        keep.dispose_mem();
        println("disposed_twice_ok");
    }
}
```

## Compile
Args: `EmperorPenguin/std/penguin/array.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `collected
keep=42
disposed_twice_ok
`
ExpectedStderr: DISCARD
