# NamespacedExternMapping
## Description
The universal extern->C rule from USER code: an extern declared in ANY namespace maps to `@<full dotted name with '.' as '_'>` — no compiler special-casing for std. This test declares `namespace _emperor { extern fun gc_info() -> i64; }`, which sanitizes to exactly the C runtime symbol `_emperor_gc_info` (also bound via `__builtin.gc_info`'s legacy `_emperor_<tail>` rule): the rule is pure name arithmetic, recomputing a runtime symbol from a user namespace. std.io's externs (std.io.file_open -> std_io_file_open) follow the identical rule. BabyPenguin's VM has no C linkage for user externs, hence Pass2/Pass3 only.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace _emperor {
    extern fun gc_info() -> i64;
}

initial {
    let bytes: i64 = _emperor.gc_info();
    println("gc-nonneg:" + cast<string>(bytes >= 0));
}
```

## Compile
Args: ``
Env: ``
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
ExpectedExitCode: 0

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `gc-nonneg:true
`
ExpectedStderr: DISCARD
