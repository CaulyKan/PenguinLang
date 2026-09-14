# ArgparseUnknownOption
## Description
An unrecognized `--option` token: parsing reports on stderr and exits 2 with the usage line. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("verbose output level", "-v", "--verbose", false, "0")
    verbose: i64 = 0;
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
Args: `--bogus`
Env: ``
Stdin: ``
ExpectedExitCode: 2
ExpectedStdout: EQUALS ``
ExpectedStderr: EQUALS `error: unknown option: '--bogus'
Usage: Options [OPTIONS]
`
