# MetaCliDefineAllFiles
## Description
CLI --define injection is visible to EVERY file (batch 2.1; the BP counterpart is the C# unit test CliDefinesAreVisibleToEveryFile): with `--define CLIDEF=1`, BOTH the fixture (file B, compiled first) and the md source (file A) must take their `#defined("CLIDEF")` then-branches. File A exposes its view through a namespace-gated function that file B calls, so one deterministic output proves both files saw the seed.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace nsa {
#if (#defined("CLIDEF")) {
    fun a_saw() -> i64 { return 1; }
} #else {
    fun a_saw() -> i64 { return 0; }
}
}
```

## Compile
Args: `--define CLIDEF=1 Tests/MetaProgramming/fixtures/define_allfiles_b.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1
b-sees
`
ExpectedStderr: DISCARD
