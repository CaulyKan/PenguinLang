# ArgparseBasic
## Description
argparse stdlib basics: `#arg`/`#pos_arg` field annotations, `std.parse_args<T>()` dogfood. Exercises options in all forms (`-v 3` short-with-next-value, `--output=x.txt` long-with-inline-value, `-f` bool switch) and a `mut std.Vector<string>` positional collecting the rest, plus reading every parsed value back. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Options {
    #arg("verbose output level", "-v", "--verbose", false, "0")
    verbose: i64 = 0;

    #arg("output path", "-o", "--output", false, "")
    out_path: string = "";

    #arg("enable fast mode", "-f", "--fast", false, "")
    fast: bool = false;

    #pos_arg("input files to process")
    files: mut std.Vector<string>;
}
initial {
    let opts: mut Options = std.parse_args<Options>();
    println("verbose=" + cast<string>(opts.verbose));
    println("out=" + opts.out_path);
    println("fast=" + cast<string>(opts.fast));
    println("files=" + cast<string>(opts.files.size()));
    let i: mut u64 = 0;
    while (i < opts.files.size()) {
        println("file[" + cast<string>(i) + "]=" + opts.files.at(i).some);
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
Args: `-v 3 --output=x.txt -f a.penguin b.penguin`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `verbose=3
out=x.txt
fast=true
files=2
file[0]=a.penguin
file[1]=b.penguin
`
ExpectedStderr: DISCARD
