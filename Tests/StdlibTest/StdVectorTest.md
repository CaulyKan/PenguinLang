# StdVectorTest
## Description
End-to-end test of the stdlib `std.Vector<T>` from `EmperorPenguin/std/penguin/vector.penguin` (NOT auto-loaded; passed via Compile.Args). Growable contiguous buffer: starts empty, first `push` allocates cap 8, then doubles (8→16→32). Pushing 17 elements forces both resize steps. Exercises push/at (bounds-checked, out-of-range → Option.none)/set/size/capacity, for-loop iteration via the independent `_VectorIterator<T>` (sum), and `dispose_mem()`. Pass3-only (pointer IR intrinsics; EmperorPenguin-native).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let v = new std.Vector<i64>();
    let i: mut u64 = 0;
    while (i < 17) { v.push(cast<i64>(i) * 10); i = i + 1; }
    println("size=" + cast<string>(v.size()));
    println("cap=" + cast<string>(v.capacity()));
    println("at0=" + cast<string>(v.at(0).some));
    println("at8=" + cast<string>(v.at(8).some));
    println("at16=" + cast<string>(v.at(16).some));
    if (v.at(17).is_none()) { println("at17=none"); }
    v.set(1, 111);
    println("at1=" + cast<string>(v.at(1).some));
    let sum: mut i64 = 0;
    for (let x in v) { sum = sum + x; }
    println("sum=" + cast<string>(sum));
    v.dispose_mem();
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
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `size=17
cap=32
at0=0
at8=80
at16=160
at17=none
at1=111
sum=1461
`
ExpectedStderr: DISCARD
