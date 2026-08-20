# MutRefGlobalGcRoot
## Description
Regression for the GC-root registration hole: `bound_type_to_ir_type` spells a
MUTABLE class binding `mut ref<X>`, but `LLVMEmitter.is_ref_type` only matched
the bare `ref<` prefix — so every `mut` reference-class GLOBAL (e.g. the meta
host's `pending_specialize_args` / `active_model`) was silently skipped by
emit_main's `_emperor_gc_add_root` registration. Such a global is only
stack-reachable while its setting frame is live; the first collection after
that frame returns sweeps the object and leaves the global dangling — observed
as the MetaTemplateValueClass pass3 SIGSEGV / silent N=0 re-specialization in
the meta JIT path. Here the only reference to a `mut` class global is the
global itself while `churn()` allocates enough garbage to force multiple GC
cycles; the box must survive with its value intact. BabyPenguin/CS run on the
.NET heap and pass trivially; the test guards the native emitter. Root cause:
commit 2c01777's "mut ref<" spelling vs the un-normalizing is_ref_type.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class RootedBox {
    v: mut i64 = 0;
}
let gbox: mut RootedBox = new RootedBox();
fun churn() {
    let i: mut i64 = 0;
    while (i < 200000) {
        let t = new RootedBox();
        t.v = i;
        i = i + 1;
    }
}
initial {
    gbox.v = 7;
    churn();
    println("alive=" + cast<string>(gbox.v));
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
ExpectedStdout: EQUALS `alive=7
`
ExpectedStderr: DISCARD
