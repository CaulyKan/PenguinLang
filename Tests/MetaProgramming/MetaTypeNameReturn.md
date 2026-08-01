# MetaTypeNameReturn
## Description
R4 composite: a `#fun` takes a `type` arg, calls `t.display_name()` (string — can't return string directly due to trampoline limits), so it returns the field COUNT as i64 while also printing the type name via `#warn`. Exercises: reflection type name + diagnostic emission + type arg. The `#warn` output doesn't fail compilation. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Vec3 { x: mut f64; y: mut f64; z: mut f64; }
#fun describe(t: type) -> i64 {
    #warn("describing type: " + t.display_name());
    return cast<i64>(t.fields().size());
}
initial {
    let n = #describe(#typeof(Vec3));
    println("fields=" + cast<string>(n));
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
ExpectedStdout: EQUALS `fields=3
`
ExpectedStderr: DISCARD
