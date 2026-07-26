# MetaTypeReturn
## Description
Phase 6 v2 (Phase 5): a `#fun -> type` called in a TYPE-SPECIFIER position is JIT-executed; the caller-stub stores the returned real `BoundType` into the host global `active_type_result`, and the host splices it as the type. `#num_or_str(0)` returns `#typeof(i64)`, so `let n: #num_or_str(0) = 5` only type-checks if the spliced type is `i64`; `#num_or_str(1)` returns `#typeof(string)`, so `let s: #num_or_str(1) = "hello"` only if `string`. A wrong resolution would fail compilation. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun num_or_str(which: i64) -> type {
    if (which == 0) { return #typeof(i64); }
    return #typeof(string);
}
initial {
    let n: #num_or_str(0) = 5;
    let s: #num_or_str(1) = "hello";
    println("n=" + cast<string>(n) + " s=" + s);
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
ExpectedStdout: EQUALS `n=5 s=hello
`
ExpectedStderr: DISCARD
