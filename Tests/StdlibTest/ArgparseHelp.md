# ArgparseHelp
## Description
`--help` prints the full help to stdout and exits 0: the usage line (options marker + positional spelling with `...` for std.Vector fields), one line per option (flags padded to 20 columns, help text, `(default: …)` / `(required)` suffixes), the reserved `-h, --help` line, and a Positional arguments section. Byte-exact. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("verbose output level", "-v", "--verbose", false, "0")
    verbose: i64 = 0;

    #arg("output path", "-o", "--output", true, "")
    out_path: string = "";

    #pos_arg("input files to process")
    files: mut std.Vector<string>;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println("ok");
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `--help`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `Usage: Options [OPTIONS] <files>...

Options:
  -v, --verbose       verbose output level (default: 0)
  -o, --output        output path (required)
  -h, --help          show this help message

Positional arguments:
  files               input files to process
`
ExpectedStderr: DISCARD
