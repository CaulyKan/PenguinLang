# MetaCompilerOptions
## Description
The compile-time option store via the `#compiler()` facade: `set_option("FOO", "bar")` then `has_option("FOO")` / `get_option("FOO")` read it back; `get_option("MISSING")` on an unset key returns `""`. The `#fun` returns 2 (the MISSING-branch) after exercising the set/read path. Same store as `#define`/`#option`/`#defined` (they share `active_meta`'s option_keys/values). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun opt_probe() -> i64 {
    compiler().set_option("FOO", "bar");
    let mut score = 0;
    if (compiler().has_option("FOO")) {
        if (compiler().get_option("FOO") == "bar") { score = score + 1; }
    }
    if (compiler().get_option("MISSING") == "") { score = score + 2; }
    return score;
}
initial {
    println("opt=" + cast<string>(#opt_probe()));
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
ExpectedStdout: EQUALS `opt=3
`
ExpectedStderr: DISCARD
