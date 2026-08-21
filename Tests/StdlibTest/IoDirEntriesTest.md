# IoDirEntriesTest
## Description
Directory operations: mkdir creates a directory, is_dir/is_file classify it, dir_entries lists exactly the one file created inside (order is filesystem-defined, so the test uses a single entry), an empty directory yields an empty string, and split_lines + for-in iterate the '\n'-joined listing.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    if (std.io.mkdir("sub")) { std.io.println("mkdir"); }
    if (std.io.is_dir("sub") && !std.io.is_file("sub")) { std.io.println("isdir"); }
    std.io.write_text("sub/only.txt", "x");
    let names: string = std.io.dir_entries("sub");
    std.io.println("one:" + names);
    if (std.io.mkdir("emptyd")) {
        std.io.println("empty:" + cast<string>(string_length(std.io.dir_entries("emptyd"))));
    }
    for (let entry in std.io.split_lines(names)) {
        std.io.println("entry:" + entry);
    }
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
ExpectedStdout: EQUALS `mkdir
isdir
one:only.txt
empty:0
entry:only.txt
`
ExpectedStderr: DISCARD
