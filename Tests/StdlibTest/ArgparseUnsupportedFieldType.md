# ArgparseUnsupportedFieldType
## Description
Negative: annotating a field whose type argparse cannot parse from the command line (a user class) is a compile-time error — the #argparse_parse generator reports the field and the supported set via compiler().error. Compile must fail; no Run section. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Inner {
    x: i64 = 0;
}
class Options {
    #arg("nested object", "-n", "--nested", false, "")
    nested: Inner = new Inner();
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println(cast<string>(opts.nested.x));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `unsupported type`
