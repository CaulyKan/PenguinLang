# IoWholeFileTest
## Description
Whole-file helpers: write_text reports success, read_text round-trips the content, size reports the byte count, read_text of a missing path is none, and the exists/is_file/is_dir/is-file-not-dir queries behave.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    if (std.io.write_text("w.txt", "alpha")) { std.io.println("wrote"); }
    let c = std.io.read_text("w.txt");
    if (c.is_some()) { std.io.println("c:" + c.some); }
    std.io.println("size:" + cast<string>(std.io.size("w.txt")));
    if (std.io.read_text("nope.txt").is_none()) { std.io.println("missing"); }
    if (std.io.is_file("w.txt") && !std.io.is_dir("w.txt")) { std.io.println("isfile"); }
    if (std.io.exists("w.txt") && !std.io.exists("nope.txt")) { std.io.println("exists"); }
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `wrote
c:alpha
size:5
missing
isfile
exists
`
ExpectedStderr: DISCARD
