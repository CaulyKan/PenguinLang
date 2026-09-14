# ArgparseForms
## Description
All option value forms: short-with-next-token (`-v 3`), long-with-inline-value (`--ratio=2.5`), short-with-inline-value (`-c=7`), plus non-i64 scalar conversions (f64 via string_to_double, u32 via cast<u32>). Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("verbose level", "-v", "--verbose", false, "0")
    verbose: i64 = 0;

    #arg("ratio", "-r", "--ratio", false, "1.0")
    ratio: f64 = 1.0;

    #arg("count", "-c", "--count", false, "0")
    count: u32 = 0;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println("v=" + cast<string>(opts.verbose));
    println("r=" + cast<string>(opts.ratio));
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
Args: `-v 3 --ratio=2.5 -c=7`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `v=3
r=2.5
c=7
`
ExpectedStderr: DISCARD
