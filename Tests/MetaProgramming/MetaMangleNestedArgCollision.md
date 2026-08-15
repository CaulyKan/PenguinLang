# MetaMangleNestedArgCollision
## Description
RED SENTINEL (known bug, kept as a stable failure until fixed): the generic-arg mangler (`mangle_generic_name`, SemanticModel.penguin) sanitizes each arg's display name by mapping `<`, `>`, `,`, and space to `_` and joins args with `__`, which is NOT injective. A NESTED generic arg collides with any flat type whose identifier name equals the sanitized nested spelling: `Wrap<B<C>>` (arg display `B<C>` sanitizes to `B_C_` — the trailing `>` becomes a trailing underscore) and `Wrap<B_C_>` (a class literally named `B_C_`) both mangle to `Wrap__B_C_`. The second instantiation then wrongly dedups to the first's specialization (`is_instantiation_already_exists` finds the mangled symbol), so `Wrap<B_C_>` runs `B<C>`'s body — the second println must print `flat` but prints `nested`. Verified: BabyPenguin green (its C# mangler is injective here), EmperorPenguin Pass2/Pass3 red. Should turn green once mangled per-arg components are encoded unambiguously (e.g. length-prefixed or `_`-escaped so the arg-name mapping is injective).

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class C {}
#template(X: type) class B { fun tag(this) -> string { return "nested"; } }
class B_C_ { fun tag(this) -> string { return "flat"; } }
#template(T: type) class Wrap {
    v: T;
    fun new(mut this, v: T) { this.v = v; }
    fun tag(this) -> string { return this.v.tag(); }
}
initial {
    println(new Wrap<B<C>>(new B<C>()).tag());
    println(new Wrap<B_C_>(new B_C_()).tag());
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
ExpectedStdout: EQUALS `nested
flat
`
ExpectedStderr: DISCARD
