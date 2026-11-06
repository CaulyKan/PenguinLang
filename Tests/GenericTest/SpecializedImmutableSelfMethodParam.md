# SpecializedImmutableSelfMethodParam
## Description
A method on a generic class taking a plain UNTYPED parameter named `self` (`fun stamp(self, v: T)`) — note `self` here is an ordinary identifier, NOT the `this` receiver (the parser pre-types `this` as `Self`, so it is unaffected). The historical bug was two-sided: EmperorPenguin pass1/2/3 emitted a malformed `call ...(void %t1)` at the call site (fixed earlier in O6-D at the emission layer: the coercion param table now skips void-as-unit params, with a decoupled slot counter); BabyPenguin crashed with a NullReferenceException in SymbolElaboratePass (03_SymbolElaborate.cs:213 `param.TypeSpecifier!.Name`, code tagged "TODO: type inference").

FIXED on BabyPenguin (O6-F, 2026-11-05) by semantic-layer elision: an untyped non-`this` parameter has no initializer to infer from, and EmperorPenguin's observable behavior is that such a parameter does not exist in the calling convention (void-as-unit elision) — so SymbolElaboratePass now skips it entirely (no FunctionParameter, no parameter symbol, remaining parameters renumber), matching the observable behavior on all compilers. A body that READS the untyped parameter name now reports a clean E_RESOLVE_SYMBOL on BabyPenguin (EmperorPenguin compiles it with a void value — a pathological shape, recorded here for the record). Expected: exit 0, `x`.
## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class Holder {
    fun stamp(self, v: T) -> string {
        return "x";
    }
}
initial {
    let mut h = new Holder<i32>();
    println(h.stamp(41));
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
ExpectedStdout: EQUALS `x
`
ExpectedExitCode: 0
