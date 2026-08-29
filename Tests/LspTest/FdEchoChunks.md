# FdEchoChunks
## Description
External fd event sources (LSP stdio runtime, L1): a coroutine parks on stdin readability via `__builtin._fd_wait_read(0)`, drains one chunk per wake with `__builtin._read_fd(0)` and echoes it to fd 1 until EOF (empty read after a wake), then prints a terminator and lets the program end at quiescence. This locks in the scheduler's fd integration end to end: quiescence must NOT exit while an fd waiter is parked (a server waiting on its stdin is a legitimate steady state), readiness must inject the next delta round, and EOF (POLLHUP after the writer closes the pipe) must wake the reader so it can finish. The fd-park externs also drive `has_suspension` from a plain call site (emit_main runs initials on the coroutine scheduler even though the program contains no `wait` keyword).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    while (true) {
        __builtin._fd_wait_read(0);
        let c : string = __builtin._read_fd(0);
        if (c == "") {
            break;
        }
        __builtin._write_fd(1, c);
    }
    __builtin._write_fd(1, "[eof]\n");
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: `hello\nworld\n`
ExpectedExitCode: 0
ExpectedStdout: EQUALS `hello
world
[eof]
`
ExpectedStderr: DISCARD
