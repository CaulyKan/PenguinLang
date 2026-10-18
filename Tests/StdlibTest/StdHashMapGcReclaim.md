# StdHashMapGcReclaim
## Description
Reclamation of a dead container graph: a `std.HashMap<i64,i64>` becomes unreachable with its three inner `std.Vector`s (occupied/keys/values) and their native `array<T>` element arrays. Since the native-arrays round the containers hold no raw memory — the element arrays are ordinary GC objects traced precisely via their REFARR ref-maps — an unreachable HashMap graph is reclaimed wholesale by an ordinary `gc_collect()` with no finalizer running at all. The test asserts `gc_info()` DROPS across the collect (the map plus its vectors and arrays die) and that a fresh HashMap keeps working afterwards. Pass3-only (hashmap.penguin is bootstrap-deferred stdlib passed via Compile.Args).

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
        let before: i64 = gc_info();
        gc_collect();
        let after: i64 = gc_info();
        if (after < before) {
            println("collected");
        } else {
            println("no_collect");
        }
        let mut m2 = new std.HashMap<i64, i64>();
        m2.put(1, 10);
        m2.put(2, 20);
        println("m2_get=" + cast<string>(m2.get(1).some));
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
