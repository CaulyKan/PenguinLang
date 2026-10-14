# WaitChangePortMt
## Description
The port-watching shape (`wait change(s.line)` was REMOVED in M0) migrated to plain transaction waits, pinned on the MT scheduler (EmperorPenguin Pass3 + `--enable-multithread-scheduler --enable-clock-model`). Same observable semantics as the aligned BabyPenguin scheduler (TimingTest/WaitChangePort): a port transaction wait delivers the DECLARED `= true` seed at time 0 (do_wait polls first), so v1 consumes the seed ("edge1" at delta 1) and v2 catches the tick-2 write(false) — "false". The classic native scheduler skips the seed (park-first, TimingTest/WaitChangePortClassic). Bug this pins: pass3-mt used to HANG on this program (the second fanout-slot wait was never woken after the timer-driven writes — recorded as a known gap when the M0 migration landed); the park-filing protocol fixes (boundary self-healing + no-consume futures) fixed it — verified 8/8 across EMPEROR_THREADS=4/8 before pinning, stable in every delta order (this is a deferred-commit shape, immune to EMPEROR_SHUFFLE_DELTAS reordering).

## Apply To
* EmperorPenguin Pass3

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
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
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
