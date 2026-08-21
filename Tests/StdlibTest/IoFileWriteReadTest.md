# IoFileWriteReadTest
## Description
std.io.File stream handle: open "w", write_line (appends \n) + write (raw) + flush + close; reopen "r", read_line sequence — final unterminated line ("world", no trailing newline) delivered once, then none at EOF; is_open flips false after close.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let f: mut std.io.File = std.io.open("f1.txt", "w");
    if (!f.is_open()) {
        std.io.println("open-failed");
    } else {
        f.write_line("hello");
        f.write("world");
        f.flush();
        f.close();
    }
    if (!f.is_open()) { std.io.println("closed"); }
    let r: mut std.io.File = std.io.open("f1.txt", "r");
    let l1 = r.read_line();
    if (l1.is_some()) { std.io.println("1:" + l1.some); }
    let l2 = r.read_line();
    if (l2.is_some()) { std.io.println("2:[" + l2.some + "]"); }
    let l3 = r.read_line();
    if (l3.is_none()) { std.io.println("3:none"); }
    r.close();
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
ExpectedStdout: EQUALS `closed
1:hello
2:[world]
3:none
`
ExpectedStderr: DISCARD
