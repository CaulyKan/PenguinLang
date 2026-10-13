# MtWireCollapseTwoTakers
## Description
Wire collapse + competing consumers in one shape: two writes in the SAME delta collapse to the final value (one transaction), the next delta's write is a second transaction — two single-take consumers drain the two transactions in order; which consumer gets which is nondeterministic, the sum is not (2+3=5, the design's set-comparison mode). Pins that same-delta collapse loses the shadowed value (1) entirely for takers while the second write lands as its own transaction — and that the directed wake clears BOTH parked takers per boundary commit with stale handles no-op'ing.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
async fun taker(ch: mut __builtin.LatestChannel<i64>) -> i64 {
    let v : i64 = wait ch;
    return v;
}

initial {
    let ch : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
    let a = async taker(ch);
    let b = async taker(ch);
    wait 0;
    ch.write(1);
    ch.write(2);
    wait 0;
    ch.write(3);
    let va : i64 = wait a;
    let vb : i64 = wait b;
    println("collapse " + cast<string>(va + vb));
    exit(0);
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
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `collapse 5
`
ExpectedStderr: DISCARD
