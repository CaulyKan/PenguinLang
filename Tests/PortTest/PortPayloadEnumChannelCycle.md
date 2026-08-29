# PortPayloadEnumChannelCycle
## Description
Regression lock (was a RED sentinel, fixed 2026-08-27): a port whose payload is a user-defined type (here an enum `E`, verified identical with an `impl IReferenceType` class). The bug: an output port's injected `_Fanout<T>` field default is synthesized by the RTL desugar, so it was invisible to the pass-3 monomorphize fixpoint's AST collector and was only specialized on demand at pass 8 (bind_new_expr). That creation cascaded into `_ChanList<LatestChannel<E>>` → `_ChanNode<LatestChannel<E>>` whose def never reached pass-7 classification; the emitter defaults an unclassified class to a VALUE class and the recursive field shape (`_ChanNode.next: Option<_ChanNode<T>>`) died as `error[E_SIZE_CYCLE]` at LLVM layout time. Fix (MonomorphizePass.complete_late_specialization / finish_late_spec_def / late_ensure_def_signatures + SemanticModel.catch_up_def_before_bodies_as_pass8): on-demand pass-8 specializations are centrally force-completed with signature passes and method-signature instantiation walks, without double-running caller recipes (double-binding re-adds method `this` parameters — LLVM "redefinition of argument '%this'"). Verified green on Pass1/Pass2/Pass3. NOTE BabyPenguin is NOT in Apply To: it fails the same shape through a DIFFERENT, independent bug (string-based generic-arg comparison rejects `!mut E` flavored args in IFuture.do_wait). Correct behavior: the program compiles and exits 0 silently (the modules idle). The LSP server's string-only port payloads are now an architectural choice (wire data is text), no longer a compiler workaround.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
enum E { a: i64; }
class Echo {
    input c : E;
    output f : E;
    initial {
        while (true) {
            let s : E = wait this.c;
            if (s is E.a) {
                this.f.write(new E.a(s.a + 1));
            }
        }
    }
}
construct {
    let seed : mut __builtin.LatestChannel<E> = new __builtin.LatestChannel<E>();
    let e1 : mut Echo = new Echo();
    let e2 : mut Echo = new Echo();
    connect(seed, e1.c);
    connect(e1.f, e2.c);
}
initial {
    seed.write(new E.a(1));
    wait 3 tick;
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS ``
ExpectedStderr: DISCARD
