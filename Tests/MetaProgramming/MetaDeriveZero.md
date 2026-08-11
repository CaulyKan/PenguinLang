# MetaDeriveZero
## Description
R4 composite: `#derive_zero(#typeof(Vec3))` — iterates `t.fields()` (names via AST fallback), builds a function that creates a zeroed instance, `compiler().create_definition(computed)` injects it. Exercises reflection + computed string codegen + def-splice in combination. `zero_v()` creates Vec3(0,0,0). Requires native Pass2/Pass3.

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
