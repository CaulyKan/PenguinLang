# StdHashMapAutoDispose
## Description
GC auto-finalization over a dead container graph: a `std.HashMap<i64,i64>` becomes unreachable with its three inner `std.Vector`s (occupied/keys/values). Sweep runs in two phases — all dead objects are unlinked first, then finalizers run, then frees — so HashMap.dispose_mem() may safely touch its (still-allocated) inner Vectors, disposing them before their own finalizers run. The inner Vectors' dispose_mem must therefore be idempotent (buf!=0 guard + zeroing); a double free would corrupt the heap and crash. After collection, a fresh HashMap must keep working. Pass3-only (hashmap.penguin is bootstrap-deferred stdlib passed via Compile.Args).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    fun make_map_garbage() {
        let mut m = new std.HashMap<i64, i64>();
        let i: mut i64 = 0;
        while (i < 20) {
            m.put(i, i * 2);
            i = i + 1;
        }
        println("made=" + cast<string>(m.size()));
    }

    initial {
        make_map_garbage();
        gc_collect();
        println("collected");
        let mut m2 = new std.HashMap<i64, i64>();
        m2.put(1, 10);
        m2.put(2, 20);
        println("m2_get=" + cast<string>(m2.get(1).some));
        m2.dispose_mem();
        println("done");
    }
}
```

## Compile
Args: `EmperorPenguin/std/penguin/hashmap.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `made=20
collected
m2_get=10
done
`
ExpectedStderr: DISCARD
