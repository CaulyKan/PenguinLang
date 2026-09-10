# GcGenerationalVectorChurn
## Description
Generational GC (GC v2 phase 3b) typed-buffer guard. `EMPEROR_GC_MODE=precise` enables the nursery and `EMPEROR_GC_YOUNG=65536` forces a minor collection every 64KB of young allocation, so the churn loop below triggers dozens of minors whose only cover for the Vector's raw malloc'd element buffer is the TYPED registration (`#__track_buffer` — vector.penguin registers (buf, cap, #sizeof(T), element ref-map) instead of the legacy conservative byte region). A minor REWRITES young element references in place (promotion moves the strings); a missing or mistyped registration leaves elements pointing into recycled nursery chunks and the total comes out wrong or crashes. The kept strings are also re-read only AFTER the churn, so every element must have survived repeated promotion + rewrite cycles. Pass3-only (vector.penguin is bootstrap-deferred stdlib passed via Compile.Args).

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
    while (i < 8) {
        keep.push(pad(1000));
        i = i + 1;
    }
    gc_collect();
    let j: mut i64 = 0;
    while (j < 30000) {
        let tmp: string = "garbage" + cast<string>(j);
        j = j + 1;
    }
    gc_collect();
    let total: mut i64 = 0;
    let k: mut i64 = 0;
    while (k < cast<i64>(keep.size())) {
        let s: string = keep.at(cast<u64>(k)).some;
        total = total + string_length(s);
        k = k + 1;
    }
    println(cast<string>(total));
    println("ok");
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
Env: `EMPEROR_GC_MODE=precise EMPEROR_GC_YOUNG=65536`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `128000
ok
`
ExpectedStderr: DISCARD
