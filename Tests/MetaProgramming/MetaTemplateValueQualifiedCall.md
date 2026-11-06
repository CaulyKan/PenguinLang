# MetaTemplateValueQualifiedCall
## Description
A QUALIFIED value-generic function call — `app.stamped<4>()` where `stamped` is `#template(N: i32) fun stamped()` in namespace `app`. This is the only cross-file spelling of a value-template call (file isolation hides the bare name), so it must work on EmperorPenguin.

FIXED (O6-G, 2026-11-05) on the EmperorPenguin side, all three layers: the parser's postfix member-access arm now falls back to value-expression generic args when type args don't apply (ns.f<4>() no longer degrades into a `<` comparison); the monomorphize fixpoint collects the request from the member's value_generic_args (recorded under the bare member name, like the type-generic member arm); and bind_function_call's D6 rewrite handles the qualified callee — it resolves the template's full name through the qualified base (resolve_qualified), mangles against it, and re-binds as a plain namespace call to the specialization (which lands in the template's namespace scope). Verified: single-file, CROSS-FILE (def and call in different files), nested namespaces (`outer.inner.deep<5>()`), value args plus call arguments (`app.scale<3>(14)`).

BabyPenguin is NOT in Apply To, consistent with the whole value-template family (MetaTemplateValueBox/Bool/Double/String/Class/Enum/... are all Pass2/3-only): BabyPenguin's ANTLR grammar has no value-generic call syntax at all — even the UNQUALIFIED same-file form `stamped<4>()` is a parse error there. That is a platform-wide gap of the C# reference implementation, not specific to the qualified spelling.

Previous bug for the record: the EP parser accepted the form but bind_function_call's D6 value-template path only rewrote `Expression.identifier` callees — the qualified callee fell through to a plain member bind that resolved the TEMPLATE symbol, and the emitter died with `error[E_INTERNAL]: function-value reference to unknown function: app.stamped` (LLVMCalls:266). The unqualified same-file form always worked everywhere (MetaTemplateValueBox).
## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace app {
#template(N: i32)
fun stamped() -> i64 {
    return N;
}
}
initial {
    println("value=" + cast<string>(app.stamped<4>()));
}
```
## Compile
Args: `--disable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedStdout: EQUALS `value=4
`
ExpectedExitCode: 0
