# MetaTemplateValueLetAnnotation
## Description
Value-template parameters reached through TYPE-ANNOTATION positions of local lets — the resolve-chain path (resolve_type_specifier -> try_resolve_template_instantiation, the M4 use-site routing). The pass-3 collectors scan expressions and signature/field/global surfaces but never the `let x: T = ...` annotation of a function-body local, so these mentions are first resolved during pass-8 body binding where M4 is the routing that confirms (or, for genuinely uncollected mentions, creates) the specialization: `let a: mut A<5> = make();` (annotation-only mention; the initializer's own signature `-> mut A<5>` is a separate, collected surface) and `let b: mut A<#n()> = ...` (a M6-step2 meta-call value arg: `#fun n() -> i32` JIT-evaluated into an int template arg). Both must bind to the SAME specialization the annotation spells: literal `A<5>` (get -> 5) and meta-computed `A<6>` (get -> 6). This locks the value-param-template resolve chain against the A3/A4 speculative-window rework (batch 3.4 checklist) — note it is a behavioral lock, not a rigid M4-only proof: with M4 absent the annotations would degrade to the template+args representation and lean on the is_same_type unification, so the guarantee this test enforces is that WHATEVER routing owns pass-8 type annotations keeps binding them to the spelled specialization. Meta JIT is required (meta-call arg evaluation). Pass3-only, and the pass2 limitation is now fully attributed (bugfix round recon, replacing the former "unit-B seed" guess): build/bootstrap/pass2 is the PASS1-PROJECT compiler (Makefile: `$(BS)/pass2.ll: $(BABY_CS) $(EP1_SRC)`) — MetaConfigStub's `meta_runtime_available() -> false` is compiled into it (verified: pass2.ll has `define i8 @emperor_meta_runtime_available() { ... return 0 }`), so every gate behind that flag — M1 `_template_X` synthesis, M4 use-site routing, M6-step2 meta-call value-arg evaluation (SemanticBindMetaCalls try_eval_meta_value_arg/try_resolve_template_instantiation) — is OFF on pass2 by construction, even though the binary is JIT-linked (-enable-meta). The `A<#n()>` arg therefore declines, falls into the UNGATED type-position fallback (try_resolve_meta_type_specifier), whose stub JIT-compiles `#n` fine but reads the void TYPE channel → the observed `E_UNSUPPORTED 'did not return a known type'`. pass3 is built by pass2 from EmperorPenguinPass2.penguins (real metaconfig, returns true) → all meta gates ON → test passes. Fixing pass2 would require linkage-aware gating (CLI flag from the link script, or a non-ANTLR metaconfig digestible by BabyPenguin) with bootstrap-convergence blast radius — an infra decision, not a bugfix; until then this stays Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun n() -> i32 { return 6; }

#template(N: i32)
class A {
    foo: mut i32 = N;
    fun new(mut this, v: i32) { this.foo = v; }
    fun get(this) -> i64 { return N; }
}

fun make() -> mut A<5> { return new A<5>(7); }

initial {
    let a: mut A<5> = make();
    let b: mut A<#n()> = new A<#n()>(9);
    println("a=" + cast<string>(a.get()) + " foo=" + cast<string>(a.foo));
    println("b=" + cast<string>(b.get()) + " foo=" + cast<string>(b.foo));
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
ExpectedStdout: EQUALS `a=5 foo=7
b=6 foo=9
`
ExpectedStderr: DISCARD
