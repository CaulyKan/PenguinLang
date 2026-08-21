# IoStdinEmptyLineTest
## Description
std.io.read_line treats an EMPTY line mid-stream as some("") (not EOF): with stdin "x\n\ny\n" the reads yield "x", "", "y", then none. Guards the `len==0 && eof` conjunction — an empty line must not be swallowed as end-of-input.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a = std.io.read_line();
    let b = std.io.read_line();
    let c = std.io.read_line();
    let d = std.io.read_line();
    if (a.is_some()) { std.io.println("1:[" + a.some + "]"); }
    if (b.is_some()) { std.io.println("2:[" + b.some + "]"); }
    if (c.is_some()) { std.io.println("3:[" + c.some + "]"); }
    if (d.is_none()) { std.io.println("4:none"); }
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
Stdin: `x\n\ny\n`
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1:[x]
2:[]
3:[y]
4:none
`
ExpectedStderr: DISCARD
