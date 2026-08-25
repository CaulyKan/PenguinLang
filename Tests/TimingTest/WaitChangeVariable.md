# WaitChangeVariable
## Description
`wait change(x)` on a plain variable: parks until the variable's value differs from its entry sample, then yields the NEW value (the design's edge-detection idiom `let v = x; while (x == v) { wait x; }` as sugar) — 5 -> 9 prints 9.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let level : mut i64 = 5;

initial {
    wait 2 tick;
    level = 9;
}

initial {
    let v : i64 = wait change(level);
    println(cast<string>(v));
    exit(0);
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `9
`
ExpectedStderr: DISCARD
