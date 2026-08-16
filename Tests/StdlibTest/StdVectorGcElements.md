# StdVectorGcElements
## Description
GC visibility of references stored inside a std.Vector's RAW malloc'd element buffer: the conservative collector scans the stack and GC object bodies, but never inside plain malloc memory — Vector/HashMap/Array therefore register their buffers as GC scan regions (`__builtin._gc_scan_add/_gc_scan_remove`, added with the GC finalizer work), and Vector.dispose_mem removes the region. This test keeps 12 live ~16KB strings whose ONLY references sit inside the Vector's raw buffer, then churns garbage allocations well past the 256KB collection threshold so multiple automatic collections run while the strings are reachable solely through unregistered-unsafe memory. Without the scan-region registration every element is swept mid-churn and the total length comes out wrong (or crashes); with it the elements survive intact. Pass3-only (vector.penguin is bootstrap-deferred stdlib passed via Compile.Args; pointer IR is EmperorPenguin-native). Green on pass1/2/3 as of the fix; would turn red if the registration is ever dropped.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
fun pad(n: i64) -> string {
    let mut sb = new __builtin.StringBuilder();
    let i: mut i64 = 0;
    while (i < n) {
        sb.append("0123456789abcdef");
        i = i + 1;
    }
    return sb.to_string();
}

initial {
    let mut keep = new std.Vector<string>();
    let i: mut i64 = 0;
    while (i < 12) {
        keep.push(pad(1000));
        i = i + 1;
    }
    let j: mut i64 = 0;
    let acc: mut i64 = 0;
    while (j < 20000) {
        let tmp: string = "garbage" + cast<string>(j);
        acc = acc + string_length(tmp);
        j = j + 1;
    }
    let total: mut i64 = 0;
    let k: mut i64 = 0;
    while (k < cast<i64>(keep.size())) {
        total = total + string_length(keep.at(cast<u64>(k)).some);
        k = k + 1;
    }
    println(cast<string>(total));
    println("acc=" + cast<string>(acc));
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
ExpectedStdout: EQUALS `192000
acc=228890
`
ExpectedStderr: DISCARD
