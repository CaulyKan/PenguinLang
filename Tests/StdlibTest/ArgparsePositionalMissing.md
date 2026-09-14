# ArgparsePositionalMissing
## Description
A scalar positional is implicitly required: supplying fewer positionals than declared reports on stderr and exits 2. The message names the missing argument; the usage line has no `[OPTIONS]` (the class declares no option fields). Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Args {
    #pos_arg("source file")
    src: string = "";

    #pos_arg("destination file")
    dst: string = "";
}
initial {
    let opts: mut Args = std.parse_args<Args>();
    println(opts.src + "->" + opts.dst);
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `a.txt`
Env: ``
Stdin: ``
ExpectedExitCode: 2
ExpectedStdout: EQUALS ``
ExpectedStderr: EQUALS `error: missing required argument: <dst>
Usage: Args <src> <dst>
`
