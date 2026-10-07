# MtOwnershipAliasRejected
## Description
Negative: the construction freshness check (design doc §9.2 rule 1). `new Ownership<mut Cell>(g)` where g is a GLOBAL reference must fail the compile — the wrapped graph aliases the global, and the cross-thread exclusivity guarantee would be void. v1 accepts only: primitive literals (values, no aliases), `new` expressions, and `.move()` results; linear-held locals need an escape analysis and are a later precision step.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Cell { v: mut i64; }
let g : mut Cell = new Cell();

initial {
    let o : mut __builtin.Ownership<mut Cell> = new __builtin.Ownership<mut Cell>(g);
    println("should not compile");
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
