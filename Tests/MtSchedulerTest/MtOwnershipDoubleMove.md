# MtOwnershipDoubleMove
## Description
Negative (runtime): consuming an already-consumed wrapper is a runtime error — "Ownership.move on an already-consumed wrapper (code 101)" on stderr, exit 1. The language has no linear types, so consumption is tracked at runtime (design doc §9.2 rule 2: runtime check is sufficient).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let o : mut __builtin.Ownership<i64> = new __builtin.Ownership<i64>(7);
    let a : i64 = o.move();
    let b : i64 = o.move();
    println("unreachable " + cast<string>(a + b));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
