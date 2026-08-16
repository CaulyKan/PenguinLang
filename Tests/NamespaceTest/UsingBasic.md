# UsingBasic
## Description
`using <namespace>;` pulls a namespace's symbols into unqualified lookup for the file. Without the using, `answer()` would not resolve (namespaces are not globally visible unqualified — see UsingRequired). With it, both functions and types (the `Counter` class) resolve without qualification. EmperorPenguin-only: BabyPenguin's grammar does not parse `using` yet.

## Apply To
* EmperorPenguin Pass1

## Test Code
```
namespace helpers {
    fun answer() -> i64 { return 42; }
    class Counter {
        value: mut i64 = 0;
        fun bump(mut this) { this.value = this.value + 1; }
        fun get(this) -> i64 { return this.value; }
    }
}
using helpers;
initial {
    println("answer=" + cast<string>(answer()));
    let c: mut Counter = new Counter();
    c.bump();
    c.bump();
    println("count=" + cast<string>(c.get()));
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
ExpectedStdout: EQUALS `answer=42
count=2
`
ExpectedStderr: DISCARD
