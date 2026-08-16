# UsingInsideNamespace
## Description
A `using` directive inside a `namespace` body imports into that namespace's scope: `app.run()` can call `twice()` unqualified, while the import stays local to `namespace app` (a sibling namespace would still need qualification). EmperorPenguin-only: BabyPenguin's grammar does not parse `using` yet.

## Apply To
* EmperorPenguin Pass1

## Test Code
```
namespace util {
    fun twice(v: i64) -> i64 { return v * 2; }
}
namespace app {
    using util;
    fun run() -> i64 { return twice(21); }
}
initial {
    println("result=" + cast<string>(app.run()));
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
ExpectedStdout: EQUALS `result=42
`
ExpectedStderr: DISCARD
