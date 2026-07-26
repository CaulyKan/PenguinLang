# MetaReflectionBasic
## Description
Phase 6 v2 real-pointer reuse: `#fun field_count_of(t: type)` receives a **real** `emperor.BoundType` pointer (materialized by the caller-stub via the `penguin_meta_get_type` token→pointer bridge) and calls `t.fields()` **directly** — a normal method call that weak-dedup resolves to the host's `emperor_BoundType_fields`. `Point` has fields `x`, `y`, so the count is `2`. This is the v2 form (replaces Round 1's `#field_count(t)` host-callback). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point {
    x: i32;
    y: i32;
}
#fun field_count_of(t: type) -> i64 {
    return cast<i64>(t.fields().size());
}
initial {
    println("count=" + cast<string>(#field_count_of(#typeof(Point))));
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
ExpectedStdout: EQUALS `count=2
`
ExpectedStderr: DISCARD
