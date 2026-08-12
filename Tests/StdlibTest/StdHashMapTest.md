# StdHashMapTest
## Description
End-to-end test of the stdlib `std.HashMap<K,V>` from `EmperorPenguin/std/penguin/hashmap.penguin` (NOT auto-loaded; passed via Compile.Args). Growable open-addressing hash map with linear probing + tombstones: starts at cap 8 and doubles when the load factor would exceed 0.75. Keys are hashed via the IHash interface (`k.hash()` — primitive direct dispatch for i64/string) and compared with `==`. Putting 10 entries forces the 8→16 resize; a `HashMap<string, i64>` sub-case exercises the string IHash (FNV-1a) path. Exercises put/get/contains/remove/size, for-loop iteration via the independent `_HashMapIterator<V>`, and `dispose_mem()`. Pass3-only (meta `#fun require_ihash` compile-time key check + pointer IR intrinsics; EmperorPenguin-native).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let m = new std.HashMap<i64, i64>();
    let i: mut i64 = 0;
    while (i < 10) {
        m.put(i, i * 100);
        i = i + 1;
    }
    println("size=" + cast<string>(m.size()));
    println("get0=" + cast<string>(m.get(0).some));
    println("get5=" + cast<string>(m.get(5).some));
    println("get9=" + cast<string>(m.get(9).some));
    if (m.get(99).is_none()) { println("get99=none"); }
    if (m.contains(3)) { println("contains3"); }
    m.remove(5);
    println("size_after_remove=" + cast<string>(m.size()));
    if (m.get(5).is_none()) { println("get5_after_remove=none"); }
    m.put(10, 1000);
    let sum: mut i64 = 0;
    for (let v in m.iter_values()) { sum = sum + v; }
    println("sum=" + cast<string>(sum));
    // default iter() now yields key/value pairs
    let psum: mut i64 = 0;
    for (let p in m) { psum = psum + p.value; }
    println("psum=" + cast<string>(psum));
    let kcount: mut i64 = 0;
    for (let k in m.iter_keys()) { kcount = kcount + 1; }
    println("kcount=" + cast<string>(kcount));
    m.dispose_mem();

    let ms = new std.HashMap<string, i64>();
    ms.put("apple", 1);
    ms.put("banana", 2);
    ms.put("cherry", 3);
    ms.put("date", 4);
    ms.put("elderberry", 5);
    ms.put("fig", 6);
    ms.put("grape", 7);
    ms.put("honeydew", 8);
    println("string_size=" + cast<string>(ms.size()));
    println("apple=" + cast<string>(ms.get("apple").some));
    println("honeydew=" + cast<string>(ms.get("honeydew").some));
    if (ms.get("kiwi").is_none()) { println("kiwi=none"); }
    if (ms.contains("banana")) { println("contains_banana"); }
    ms.remove("banana");
    println("string_size_after_remove=" + cast<string>(ms.size()));
    let ssum: mut i64 = 0;
    for (let v in ms.iter_values()) { ssum = ssum + v; }
    println("string_sum=" + cast<string>(ssum));
    ms.dispose_mem();
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
ExpectedStdout: EQUALS `size=10
get0=0
get5=500
get9=900
get99=none
contains3
size_after_remove=9
get5_after_remove=none
sum=5000
psum=5000
kcount=10
string_size=8
apple=1
honeydew=8
kiwi=none
contains_banana
string_size_after_remove=7
string_sum=34
`
ExpectedStderr: DISCARD
