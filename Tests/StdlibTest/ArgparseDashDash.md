# ArgparseDashDash
## Description
`--` stops option parsing: everything after is positional. A negative number still works as an option's next-token value (`-o -5`), and dash-prefixed tokens after `--` (`-x`, `-3`) are collected by the std.Vector positional instead of tripping the unknown-option error. Requires native Pass2/Pass3 (argparse.penguin + vector.penguin via Compile.Args).
## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Args {
    #arg("offset", "-o", "--offset", false, "0")
    offset: i64 = 0;

    #pos_arg("values")
    values: mut std.Vector<string>;
}
initial {
    let opts: mut Args = std.parse_args<Args>();
    println("o=" + cast<string>(opts.offset));
    let i: mut u64 = 0;
    while (i < opts.values.size()) {
        println("v" + cast<string>(i) + "=" + opts.values.at(i).some);
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
Args: `-o -5 -- -x -3`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `o=-5
v0=-x
v1=-3
`
ExpectedStderr: DISCARD
