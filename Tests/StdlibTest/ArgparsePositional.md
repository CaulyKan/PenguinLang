# ArgparsePositional
## Description
Scalar positional arguments fill in declaration order (`src` then `dst`); the usage line spells them without an `[OPTIONS]` block when the class has no option fields. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

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
Args: `a.txt b.txt`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a.txt->b.txt
`
ExpectedStderr: DISCARD
