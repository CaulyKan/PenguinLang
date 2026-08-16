# FileLocalTopLevelVisible
## Description
Definitions at file top level (outside any `namespace` block) live in a per-file anonymous namespace (C++ static semantics) but remain unqualified-visible within their own file: top-level functions, classes, global variables and initial routines all resolve. Both compilers implement this (BabyPenguin via `_ns_<file>` roots; EmperorPenguin via `_ns_<stem>_<hash>` file namespaces).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
fun helper() -> i64 { return 7; }
class Counter {
    count: mut i64 = 0;
    fun bump(mut this) { this.count = this.count + 1; }
}
let magic: i64 = 41;
initial {
    let c: mut Counter = new Counter();
    c.bump();
    println("helper=" + cast<string>(helper()) + " magic=" + cast<string>(magic + 1) + " count=" + cast<string>(c.count));
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
ExpectedStdout: EQUALS `helper=7 magic=42 count=1
`
ExpectedStderr: DISCARD
