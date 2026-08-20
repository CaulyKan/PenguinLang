# MetaDeriveZero
## Description
R4 composite: `#derive_zero(#typeof(Vec3))` — iterates `t.fields()` (names via AST fallback), builds a function that creates a zeroed instance, `compiler().create_definition(computed)` injects it. Exercises reflection + computed string codegen + def-splice in combination. `zero_v()` creates Vec3(0,0,0). Requires native Pass2/Pass3.

**RED SENTINEL (known regression on feature/value-enum-size, not on master)**:
same derive-pipeline breakage as MetaDeriveClone (E_RESOLVE_TYPE on the
injected definition's types). Green on master (verified 2026-08-19 via
/tmp/wt-master bootstrap); broken by the branch's generic-meta-args /
derive-pipeline changes (544e4bb..2c01777 era). Should turn green once that
regression is fixed. No known-good compiler applies (meta-only syntax).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Vec3 { x: mut i32; y: mut i32; z: mut i32; }
#fun derive_zero(t: type) -> ast {
    let fs = t.fields();
    let n = cast<i64>(fs.size());
    let mut body = "fun zero_v() -> Vec3 { let q: mut Vec3 = new Vec3(); ";
    let i: mut i64 = 0;
    while (i < n) {
        body = body + "q." + fs.at(cast<u64>(i)).some.name + " = 0; ";
        i = i + 1;
    }
    body = body + "return q; }";
    return compiler().create_definition(body);
}
#derive_zero(#typeof(Vec3));
initial {
    let v = zero_v();
    println(cast<string>(v.x) + "," + cast<string>(v.y) + "," + cast<string>(v.z));
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
ExpectedStdout: EQUALS `0,0,0
`
ExpectedStderr: DISCARD
