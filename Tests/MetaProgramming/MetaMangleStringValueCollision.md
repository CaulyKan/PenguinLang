# MetaMangleStringValueCollision
## Description
RED SENTINEL (known bug, kept as a stable failure until fixed): a STRING value-template argument is mangled by its raw content (`BoundTypeArgValue.display_name()` returns `string_val`), and `mangle_generic_name` sanitizes space to `_` — so two DIFFERENT string values collide: `q<"a b">()` and `q<"a_b">()` both mangle the specialization to `q__a_b`. The second call site wrongly dedups to the first's specialization and runs its body, returning `"a b"` where `"a_b"` was written. Value args are not kind-tagged or escaped in the mangle (a string arg, an int arg, and a type arg of the same spelling all produce identical mangled components), so any sanitization-ambiguous content collides. Verified red on EmperorPenguin Pass2/Pass3 (BabyPenguin's grammar does not accept string value-generic args, so it is not in Apply To). Should turn green once value-arg mangle components are encoded unambiguously (kind tag + escaping).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(S: string) fun q() -> string { return S; }
initial {
    println(q<"a b">());
    println(q<"a_b">());
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
ExpectedStdout: EQUALS `a b
a_b
`
ExpectedStderr: DISCARD
