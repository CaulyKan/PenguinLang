# MetaReflectionCount
## Description
Phase 6 v2 real-pointer reuse: `#fun`s call `t.fields()`, `t.methods()`, `t.is_class()`, `t.is_enum()` directly on the real `BoundType`. `Point` has 2 fields + 1 method (`norm`); `is_class(Point)` is true; enum `E` gives `is_enum(E)` true. Expected `2 1 1 1`. (String-returning `t.display_name()` can't be returned from a `#fun` — the JIT trampoline is i64-only — so it's exercised via computed-string codegen in `MetaComputedCreate`.) Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point {
    x: i32;
    y: i32;
    fun norm() -> i32 { return 0; }
}
enum E { A; B; }
#fun fc(t: type) -> i64 { return cast<i64>(t.fields().size()); }
#fun mc(t: type) -> i64 { return cast<i64>(t.methods().size()); }
#fun ic(t: type) -> i64 { if (t.is_class()) { return 1; } return 0; }
#fun ie(t: type) -> i64 { if (t.is_enum()) { return 1; } return 0; }
initial {
    println(cast<string>(#fc(#typeof(Point))) + " " + cast<string>(#mc(#typeof(Point))) + " " + cast<string>(#ic(#typeof(Point))) + " " + cast<string>(#ie(#typeof(E))));
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
ExpectedStdout: EQUALS `2 1 1 1
`
ExpectedStderr: DISCARD
