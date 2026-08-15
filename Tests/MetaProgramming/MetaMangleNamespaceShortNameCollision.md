# MetaMangleNamespaceShortNameCollision
## Description
RED SENTINEL (known bug, kept as a stable failure until fixed): `BoundType.display_name()` renders class/enum/interface args by their SHORT definition name (`td.get_name()`, BoundType.penguin), dropping the namespace. Two same-named types in different namespaces therefore mangle identically: `Wrap<n1.B>` and `Wrap<n2.B>` both produce `Wrap__B`, and the second instantiation wrongly dedups to the first's specialization. The wrong specialization is observable at runtime: `Wrap<n2.B>`'s field is typed `n1.B` and `this.v.tag()` statically binds `n1.B.tag`, so the second println prints `n1` instead of `n2` (the constructor even accepts the `n2.B` argument without a diagnostic because the field assignment is unchecked). Verified: BabyPenguin green (full-name mangling), EmperorPenguin Pass2/Pass3 red. Should turn green once mangled arg components use qualification-aware, unambiguous names.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace n1 { class B { fun tag(this) -> string { return "n1"; } } }
namespace n2 { class B { fun tag(this) -> string { return "n2"; } } }
#template(T: type) class Wrap {
    v: T;
    fun new(mut this, v: T) { this.v = v; }
    fun tag(this) -> string { return this.v.tag(); }
}
initial {
    println(new Wrap<n1.B>(new n1.B()).tag());
    println(new Wrap<n2.B>(new n2.B()).tag());
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
ExpectedStdout: EQUALS `n1
n2
`
ExpectedStderr: DISCARD
