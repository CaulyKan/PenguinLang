# ArgparseConversionFail
## Description
A non-numeric value for an i64 option fails the runtime validation (std._arg_is_int): stderr names the option token and the value, exit 2. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("count", "-c", "--count", false, "0")
    count: i64 = 0;

    #arg("output path", "-o", "--output", false, "")
    out_path: string = "";
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println("c=" + cast<string>(opts.count));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `--output x -c abc`
Env: ``
Stdin: ``
ExpectedExitCode: 2
ExpectedStdout: EQUALS ``
ExpectedStderr: EQUALS `error: invalid value for option '-c': 'abc'
Usage: Options [OPTIONS]
`
