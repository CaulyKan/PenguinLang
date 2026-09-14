# ArgparseNoAnnotations
## Description
Negative: `std.parse_args<T>()` on a class with NO annotated fields is a compile-time error — the #argparse_parse generator reports via compiler().error ("the class has no annotated fields"). Compile must fail; no Run section. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    verbose: i64 = 0;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println(cast<string>(opts.verbose));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `no annotated fields`
