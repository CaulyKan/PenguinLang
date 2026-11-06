# MtOwnershipSubstringClassNameFalsePositive
## Description
RED SENTINEL (pre-existing bug, NOT introduced by the batch 0.2 split — the split merely tripped over it while self-hosting): `bind_new_expr`'s Ownership construction gate substring-matches the `new` expression's type name (`string_find(expr.type_name, "Ownership") >= 0`, see `EmperorPenguin/src/bound/SemanticBindExprCastNew.penguin` bind_new_expr / git history SemanticBindExpressions.penguin). ANY user class whose NAME merely contains the substring "Ownership" (here `OwnershipRegistry`, which has nothing to do with `__builtin.Ownership`) gets its single-argument constructor rejected with `E_MUTABILITY: new Ownership<T>(v) requires a freshly built value` when the argument is not "fresh" (here a top-level `let g : i64` global — a value with no aliasing risk at all). The batch 0.2 split hit this in self-hosting: its collaborator class was originally named `BindExprOwnership`, and `new BindExprOwnership(this)` in the pass wiring failed the bootstrap with exactly this error (worked around by renaming the class to `BindExprLinear`).

Expected CORRECT behavior (asserted below): a user class named `*Ownership*` that is not `__builtin.Ownership` compiles and runs fine. BabyPenguin (the C# reference) implements no such gate and passes today; every EmperorPenguin backend runs the substring gate and is RED until the gate matches the actual builtin (`type_name == "Ownership"` or `"__builtin.Ownership<...>"`, not a substring). Should turn green once the check is narrowed.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class OwnershipRegistry {
    v: mut i64;
    fun new(mut this, x: i64) {
        this.v = x;
    }
}
let g : i64 = 5;

initial {
    let o : mut OwnershipRegistry = new OwnershipRegistry(g);
    println("ok " + cast<string>(o.v));
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
ExpectedStdout: EQUALS `ok 5
`
ExpectedStderr: DISCARD
