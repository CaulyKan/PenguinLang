# IoStdinReadLineTest
## Description
std.io.read_line over stdin with a trailing newline: two lines delivered, third read at exact EOF returns none. Validates the two-phase eof protocol (pre-check + empty-read post-check) against the C fgetc reader.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a = std.io.read_line();
    let b = std.io.read_line();
    let c = std.io.read_line();
    if (a.is_some()) { std.io.println("1:" + a.some); }
    if (b.is_some()) { std.io.println("2:" + b.some); }
    if (c.is_none()) { std.io.println("3:none"); }
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
Stdin: `aa\nbb\n`
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1:aa
2:bb
3:none
`
ExpectedStderr: DISCARD
