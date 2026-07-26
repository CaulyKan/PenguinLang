# MetaTypeArg
## Description
Phase 5c: a `#fun` takes a `type` argument and dispatches on it at compile time using `#typeof` token equality. The type arg is resolved to an opaque token and passed through the i64 trampoline; inside the `#fun`, `#typeof(i32)`/`#typeof(i64)`/`#typeof(string)` resolve to the same tokens (shared MetaEngine registry), so `t == #typeof(...)` is a correct compile-time type test. `#type_id(i32)`→1, `#type_id(i64)`→2, `#type_id(string)`→3, each JIT-spliced to a constant. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun type_id(t: type) -> i64 {
    if (t == #typeof(i32)) { return 1; }
    if (t == #typeof(i64)) { return 2; }
    if (t == #typeof(string)) { return 3; }
    return 0;
}
initial {
    let a: i64 = #type_id(i32);
    let b: i64 = #type_id(i64);
    let c: i64 = #type_id(string);
    println("a=" + cast<string>(a) + " b=" + cast<string>(b) + " c=" + cast<string>(c));
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
ExpectedStdout: EQUALS `a=1 b=2 c=3
`
ExpectedStderr: DISCARD
