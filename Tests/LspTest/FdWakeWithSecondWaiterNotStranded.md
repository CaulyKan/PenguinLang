# FdWakeWithSecondWaiterNotStranded
## Description
Bug #5 root-cause sentinel (batch 5 handover, fixed in bugfix round 2): an fd-WOKEN coroutine must run at the next scheduler pass even while ANOTHER fd waiter stays parked. Shape: a writer coroutine bursts ~4MB through the non-blocking `_write_fd` + `_fd_wait_write` park loop (the LSP StdioStream write path — mid-frame POLLOUT parks are guaranteed at pipe capacity), while a second coroutine stays fd-parked on fd 2 (stderr — the harness holds it open and never writes it; the LSP's stdin reader plays this role in production). The program can ONLY exit 0 through `__builtin.exit(0)` after the writer's offset reaches the payload end — reachable only if every POLLOUT wake re-runs the writer. The pre-fix scheduler routed fd-woken coroutines through the next-round queue: after the (B) rotation put the woken writer into the ready queue, the idle path's blocking `_fd_poll(-1)` — sitting between that rotation and the next round's execution pass — blocked on the OTHER waiter's descriptor with READY work stranded (strace-verified: the woken writer never issued its remaining write(); the client waited forever for the truncated frame's tail — the "EP-scale didOpen loses responses" deadlock). Fix: `__sched_drain_woken` enqueues directly into `_sched_ready` (mirroring the spawn drain). Pre-fix this test times out red; post-fix exit 0. stdout DISCARDed (payload size and park count are timing-dependent; exit 0 is the assertion).
## Apply To
* EmperorPenguin Pass3

## Test Code
```
fun stderr_sleeper() {
    // Parks forever: the harness holds stderr open and never writes it —
    // the persistent SECOND fd waiter that strands the woken writer under
    // the pre-fix scheduler.
    __builtin._fd_wait_read(2);
}

fun big_writer() {
    let chunk : string = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJ\n";
    let sb : mut StringBuilder = new StringBuilder();
    let i : mut i64 = 0;
    while (i < 88000) {
        sb.append(chunk);
        i = i + 1;
    }
    let data : string = sb.to_string();
    let len : i64 = string_length(data);
    let off : mut i64 = 0;
    while (off < len) {
        let n : i64 = __builtin._write_fd(1, string_substring(data, off, len - off));
        if (n < 0) {
            __builtin.exit(1);
        }
        if (n == 0) {
            __builtin._fd_wait_write(1);
        } else {
            off = off + n;
        }
    }
    // Only reachable when every POLLOUT wake re-ran this coroutine to
    // completion (the parked stderr_sleeper can never contribute progress).
    __builtin.exit(0);
}

initial { async stderr_sleeper(); }
initial { async big_writer(); }
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
Timeout: 120
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
