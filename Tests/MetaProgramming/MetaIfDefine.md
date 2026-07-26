# MetaIfDefine
## Description
Phase 5a conditional compilation: a top-level `#define` sets a compile-time option, and `#if (#defined(...))` includes a function that the initial block then calls. EP-only (the rewrite lives in EmperorPenguin's SemanticModel).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("MODE","1");
#if (#defined("MODE")) {
    fun answer() -> i64 { return 42; }
}
initial {
    println("answer=" + cast<string>(answer()));
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
`
ExpectedStderr: DISCARD
