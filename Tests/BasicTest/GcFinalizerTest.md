# GcFinalizerTest
## Description
EmperorPenguin GC finalizer wiring: a class implementing `__builtin.IMemoryDispose` gets its `dispose_mem()` invoked by the collector when a dead instance is swept (the class metadata destructor slot points at dispose_mem — same `void(ptr)` signature). __c1 churns 100 dead instances through a helper (dead frames are not scanned, so they become unreachable) and asserts almost all were finalized exactly once — a few may be retained by stale stack/register words (conservative GC), so the bound is 97..100; fewer means finalization never ran, more means double-finalization. __c2 holds a live instance across two collections and asserts its finalizer never ran. EmperorPenguin-only (gc_collect builtin + GC-driven finalization; BabyPenguin has no deterministic finalizer).

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    let finalized: mut i64 = 0;

    class Resource {
        impl __builtin.IReferenceType;
        impl __builtin.IMemoryDispose;
        fun dispose_mem(mut this) {
            finalized = finalized + 1;
        }
    }

    fun make_garbage() {
        let r = new Resource();
    }

    initial {
        let i: mut i64 = 0;
        while (i < 100) {
            make_garbage();
            i = i + 1;
        }
        gc_collect();
        if (finalized >= 97 && finalized <= 100) {
            println("ok");
        } else {
            println("bad:" + cast<string>(finalized));
        }
    }
}
namespace __c2 {
    let disposed_live: mut i64 = 0;

    class Held {
        impl __builtin.IReferenceType;
        impl __builtin.IMemoryDispose;
        fun dispose_mem(mut this) {
            disposed_live = disposed_live + 1;
        }
    }

    initial {
        let held = new Held();
        gc_collect();
        gc_collect();
        if (disposed_live == 0) {
            println("live_retained");
        } else {
            println("live_swept:" + cast<string>(disposed_live));
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
ExpectedStdout: EQUALS `ok
live_retained
`
ExpectedStderr: DISCARD
