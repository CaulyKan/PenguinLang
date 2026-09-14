# ArgparseRepeatedOption
## Description
A std.Vector option field accumulates: repeated `-I a`, `-I b`, `--include=c` all push (in order). The vector is reset to a fresh std.Vector by the generated code, so the field needs no initializer. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("include path", "-I", "--include", false, "")
    includes: mut std.Vector<string>;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println("n=" + cast<string>(opts.includes.size()));
    let i: mut u64 = 0;
    while (i < opts.includes.size()) {
        println(opts.includes.at(i).some);
        i = i + 1;
    }
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `-I a -I b --include=c`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `n=3
a
b
c
`
ExpectedStderr: DISCARD
