# MetaSrcWithStdJsonSpecializingGate

## Description
`--meta-src` / `meta-sources=[...]` + the std dyn-lib (auto-std beside a
released compiler, or an explicit --lib): instantiating any `Option<T>` fires
json.penguin's `#specializing __builtin.Option<T>` gate
`json_deserializable_type(T)` — a `#fun`, so the gate JITs through unit B.
With `with_user_sources=true`, MetaEngine.compile_meta_function synthesizes
OTHER #funs WRAPPED in their declaring namespace (`std`), but the top-level
`__specializing_*` gate wrapper (never wrapped) calls the BARE name — and the
"every other #fun" loop used to emit NO bare-name top-level forwarder for
wrapped #funs (only the PRIMARY bmf got one), so unit B failed with "Cannot
resolve symbol 'json_deserializable_type'" and the whole compile aborted.
Verified minimal (2026-10-16): Option<i64> + --meta-src + the std lib fails;
drop --meta-src (no wrap) or the std lib (no json.penguin, no gate) and it
compiles. The example-scale victim was tinyriscv compiled by a released
compiler (auto-std). FIXED the same day: the wrap loop now emits the same
synthesize_top_level_forwarder the primary path has, for every wrapped #fun
(passing json.penguin as bare SOURCE also drags in vector/hashmap
dependencies, so the repro consumes the std lib via --lib instead; the lib is
a bootstrap artifact, hence the build/bootstrap path). Requires native Pass3
(BabyPenguin has no --meta-src / meta JIT).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let x : __builtin.Option<i64> = new __builtin.Option<i64>.some(1);
    println(x.is_some());
}
```

## Compile
Args: `--meta-src Tests/fixtures/meta_unit_b_lib.penguin --lib build/bootstrap/pass4.d/libemperorpenguin-std.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `true
`
ExpectedStderr: DISCARD
