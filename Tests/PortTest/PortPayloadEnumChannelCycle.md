# PortPayloadEnumChannelCycle
## Description
RED SENTINEL (known bug, turns green once fixed): a port whose payload is a user-defined type (here an enum `E`, verified identical with an `impl IReferenceType` class). Root cause (EP): an output port's injected `_Fanout<T>` field default is synthesized by the RTL desugar, so it is invisible to the pass-3 monomorphize fixpoint's AST collector and is only specialized on demand at pass 8 (bind_new_expr). That creation cascades into `_ChanList<LatestChannel<E>>` → `_ChanNode<LatestChannel<E>>` whose def never reaches pass 7 classification; the emitter defaults an unclassified class to a VALUE class and the recursive field shape (`_ChanNode.next: Option<_ChanNode<T>>`) dies as `error[E_SIZE_CYCLE]` at LLVM layout time. Fix direction: complete on-demand pass-8 specializations centrally in `ensure_specialized_def` (resolve_pair + signature-pass catch-up), WITHOUT double-running caller recipes (double-binding re-adds method `this` parameters — LLVM "redefinition of argument '%this'"; a wrong resolve scope makes member calls emit template-shaped signatures — "call i64 ... expected 'ptr'"). NOTE BabyPenguin is NOT in Apply To: it fails the same shape through a DIFFERENT, independent bug (string-based generic-arg comparison rejects `!mut E` flavored args in IFuture.do_wait). Correct behavior: the program compiles and exits 0 silently (the modules idle). Workaround used by the LSP server meanwhile: string-only port payloads; rich types travel through explicit Fifo channels spelled as class field types (pass-3-collected).

## Apply To
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
