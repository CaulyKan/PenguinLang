# IncrementalMetaClassSurface
## Description
Bugfix #4 sentinel (batch 5 handover): the IncrementalSession's parse cache held REFERENCES into the same AST objects the meta prepass splices in place — after the first compile, a `#class` nested inside a namespace of an UNCHANGED file was already unwrapped out of the (shared) namespace children list, so a replayed compile lost the whole meta surface: `collect_meta_class_defs` found nothing, the seed fingerprint drifted (whole-seed memo MISS) and the freshly seeded engine lacked the class, so the edited file's re-spliced `#probe()` `#fun` failed unit B with "Can't resolve type specifier 'mut Cfg'" — the incremental compile CRASHED on the first didChange (repro: 2-file project, edit only touches the `initial` body). Fix: meta-bearing files (any `#` other than `#template` in their text) never replay from the parse cache — they re-parse, so the replayed compile collects the identical meta surface (seed memo HIT, same fingerprint as didOpen) and the replay stays byte-identical to the full compile by construction. This test locks the multi-round behavior in: two body-only didChange rounds over a project whose meta face lives in an unchanged second file must keep compiling and produce the final text's full-compile output (`v3` then `value=7`). Requires native Pass2/Pass3 (meta JIT).
## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun probe() -> i64 {
    let c: mut Cfg = new Cfg();
    return c.get();
}
initial {
    println("value=" + cast<string>(#probe()));
}
```
## Compile
Args: `--incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/metaclass_v2.penguin --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/metaclass_v3.penguin Tests/IncrementalTest/metaclass_meta.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedStdout: EQUALS `v3
value=7
`
ExpectedExitCode: 0
