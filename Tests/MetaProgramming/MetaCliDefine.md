# MetaCliDefine
## Description
Phase 5a: `--define DEBUG=1` on the EmperorPenguin command line seeds the compile-time option store, so `#if (#defined("DEBUG"))` takes the then-branch. (Pass2/3 arg routing: `tmp/pass2 --define DEBUG=1 <src> -o <exe>`.)

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#if (#defined("DEBUG")) {
    fun mode() -> string { return "debug"; }
} #else {
    fun mode() -> string { return "release"; }
}
initial {
    println(mode());
}
```

## Compile
Args: `--define DEBUG=1`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `debug
`
ExpectedStderr: DISCARD
