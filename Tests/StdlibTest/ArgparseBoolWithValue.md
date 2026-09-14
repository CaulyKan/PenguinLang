# ArgparseBoolWithValue
## Description
A bool switch rejects an inline value: `--verbose=true` fails on stderr with exit 2 (bool fields take no value — use the bare flag). Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("verbose", "-v", "--verbose", false, "")
    verbose: bool = false;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println("v=" + cast<string>(opts.verbose));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `--verbose=true`
Env: ``
Stdin: ``
ExpectedExitCode: 2
ExpectedStdout: EQUALS ``
ExpectedStderr: EQUALS `error: option '--verbose=true' does not take a value
Usage: Options [OPTIONS]
`
