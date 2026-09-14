# ArgparseRequiredMissing
## Description
A `#arg(..., required=true, ...)` option not supplied on the command line: parsing reports on stderr and exits 2 with the usage line. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("output path", "-o", "--output", true, "")
    out_path: string = "";

    #arg("count", "-c", "--count", false, "0")
    count: i64 = 0;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println("out=" + opts.out_path);
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `-c 5`
Env: ``
Stdin: ``
ExpectedExitCode: 2
ExpectedStdout: EQUALS ``
ExpectedStderr: EQUALS `error: missing required option: --output
Usage: Options [OPTIONS]
`
