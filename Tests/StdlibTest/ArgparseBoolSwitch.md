# ArgparseBoolSwitch
## Description
bool fields are pure switches: `-q` and `--verbose` flip their flags with no value consumed. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("verbose", "-v", "--verbose", false, "")
    verbose: bool = false;

    #arg("quiet", "-q", "--quiet", false, "")
    quiet: bool = false;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println("v=" + cast<string>(opts.verbose) + " q=" + cast<string>(opts.quiet));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `-q --verbose`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `v=true q=true
`
ExpectedStderr: DISCARD

