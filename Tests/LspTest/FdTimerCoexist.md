# FdTimerCoexist
## Description
External fd event sources combined with the virtual-time timer wheel and Event<T> broadcast: the reader coroutine echoes stdin chunks until EOF and then emits a completion event; a second initial waits that event, waits 2 ticks, and writes a marker. Expected order is deterministic (`a`, `b`, then `T`) — the fd readiness probe runs before timer advancement in the idle path, and the timer initial cannot proceed until the reader signals. Locks in: fd waiter + timer coexistence in the idle scheduler path (a zero-timeout fd poll before clock jumps so fd events are never starved by timer bursts), Event wait after an fd-driven loop, and write_fd from a non-fd coroutine.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
let done : mut __builtin.Event<i64> = new __builtin.Event<i64>();

initial {
    while (true) {
        __builtin._fd_wait_read(0);
        let c : string = __builtin._read_fd(0);
        if (c == "") {
            break;
        }
        __builtin._write_fd(1, c);
    }
    done.emit(1);
}

initial {
    let v : i64 = wait done;
    wait 2 tick;
    __builtin._write_fd(1, "T\n");
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
Stdin: `a\nb\n`
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a
b
T
`
ExpectedStderr: DISCARD
