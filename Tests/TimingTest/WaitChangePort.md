# WaitChangePort
## Description
The port-watching shape (`wait change(s.line)`, UART line-watching) migrated to plain transaction waits: `wait change(<port expr>)` is REMOVED (concurrency redesign M0 — the watched level has no wake source). Under the ALIGNED BabyPenguin scheduler (M0) a port transaction wait delivers the DECLARED `= true` seed at time 0 (do_wait polls first; Q3: a declared default is a weak deliverable transaction), so v1 consumes the seed, "edge1" prints at delta 1, and v2 catches the tick-2 write(false) — "false". The classic scheduler skipped the seed (park-first; the description's old "no time-0 seed" finding was that scheduler's behavior — kept pinned by WaitChangePortClassic on Pass3).

NOTE (known gap): pass3 + --enable-multithread-scheduler HANGS on this program (the second fanout-slot wait is never woken after the timer-driven writes) — an mt-side bug recorded in the plan; add Pass3-mt back once fixed.


## Apply To
* BabyPenguin

## Test Code
```
class Src {
    output line : bool = true;
    initial {
        wait 2;
        this.line.write(false);
        wait 2;
        this.line.write(true);
    }
}

construct {
    let s : mut Src = new Src();
}

initial {
    let v1 : bool = wait s.line;
    println("edge1");
    let v2 : bool = wait s.line;
    println(cast<string>(v2));
    exit(0);
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `edge1
false
`
ExpectedStderr: DISCARD
