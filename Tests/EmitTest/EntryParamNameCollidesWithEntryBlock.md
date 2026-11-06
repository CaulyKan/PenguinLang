# EntryParamNameCollidesWithEntryBlock
## Description
4.4 handoff bug (emit layer, found while debugging the LSP single-actor batch): a function parameter named `entry` emitted as `%entry` collides with the emitter's `entry:` block label — LLVM text IR shares one identifier namespace for blocks and values per function, so clang fails with "unable to create block named 'entry'" and the link dies. The same collision class hits LOCAL registers named like generated labels (`let entry`, `let else2` vs the `else2:` label / `br label %else2`) and the emitter's `%cond_N` values. Fix (batch 5 C1): IRFunction.make_unique_reg_name reshapes label-shaped names (`entry` exactly, `<label-prefix><digits>` for every alloc_label prefix, `cond_<digits>`) with the `_v` suffix — same precedent as the temp-shaped `t5 -> t5_v` reshape — and alloc_param routes parameters through it so signature, ARG instructions and body references agree. Asserts the corrected behavior: param `entry`, local `entry` and local `else2` all compile, link and run (green on pass1+ after the fix; clang "unable to create block named 'entry'" on unfixed emitters).
## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3
## Test Code
```
fun probe(entry: i64) -> i64 {
    return entry + 1;
}

fun locals(v: i64) -> i64 {
    let entry: i64 = v + 1;
    let else2: i64 = 0;
    if (v > 0) {
        return entry + else2;
    } else {
        return 0;
    }
}

initial {
    println(cast<string>(probe(41)));
    println(cast<string>(locals(41)));
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
ExpectedExitCode: 0
ExpectedStdout: EQUALS `42
42
`
ExpectedStderr: DISCARD
