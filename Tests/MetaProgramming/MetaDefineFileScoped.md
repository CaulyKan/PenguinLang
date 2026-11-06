# MetaDefineFileScoped
## Description
File-scoped define semantics (batch 2.1, EP side; the BP counterpart is the C# unit test DefinesDoNotLeakAcrossFiles — BabyPenguin cannot run this case because the test runner compiles only the md's own source there): file B (the fixture, compiled first via Compile.Args) must NOT see file A's code `#define("LEAKKEY","1")`. A code #define is visible from its line to the end of ITS file, never across files.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("LEAKKEY","1");
fun anchor_a() -> i64 { return 0; }
```

## Compile
Args: `Tests/MetaProgramming/fixtures/define_filescoped_b.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `isolated
`
ExpectedStderr: DISCARD
