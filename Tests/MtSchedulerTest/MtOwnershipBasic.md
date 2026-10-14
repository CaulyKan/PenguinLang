# MtOwnershipBasic
## Description
Ownership<T> v1 (design doc §9): the explicit exclusive-transfer wrapper. Construction `new Ownership<T>(v)` passes the binder's whole-graph freshness check (v = a `new` expression here); `.move()` returns the payload and consumes the wrapper (is_owned flips to false); the moved-out Cell is then used normally — the receiver became the sole holder. A second `.move()` on the consumed wrapper is a runtime error (exit 1, code 101) — covered by MtOwnershipDoubleMove; aliasing construction is rejected at compile time — MtOwnershipAliasRejected. Pass 10 treats Ownership<U> as Shareable for ANY payload U, so an Ownership argument makes its spawn STEALABLE even though the payload is a mutable reference class — the whole point: the ONLY sanctioned way to move a reference across a thread boundary (verified by MtOwnershipCrossSpawn).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Cell { v: mut i64; }

initial {
    let o : mut __builtin.Ownership<mut Cell> = new __builtin.Ownership<mut Cell>(new Cell());
    let c : mut Cell = o.move();
    c.v = 42;
    println("moved " + cast<string>(c.v) + " owned=" + cast<string>(o.is_owned()));
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
ExpectedStdout: EQUALS `moved 42 owned=false
`
ExpectedStderr: DISCARD
