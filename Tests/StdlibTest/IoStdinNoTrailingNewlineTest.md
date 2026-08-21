# IoStdinNoTrailingNewlineTest
## Description
std.io.read_line on stdin WITHOUT a trailing newline: the final unterminated line is delivered exactly once, the next read returns none. This is the classic feof() false-positive trap — a naive eof-after-read check would either lose the line or duplicate none.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a = std.io.read_line();
    let b = std.io.read_line();
    if (a.is_some()) { std.io.println("1:[" + a.some + "]"); }
    if (b.is_none()) { std.io.println("2:none"); }
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
Stdin: `tail`
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1:[tail]
2:none
`
ExpectedStderr: DISCARD
