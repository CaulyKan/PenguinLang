# MtOwnershipLinearLocalPassedOut
## Description
The escape analysis's second negative twin: PASSING the local to a call is an escape (the callee could store it) — `take(l)` makes l not linearly held; the wrap must fail to compile.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
fun take(l : Builder) -> i64 {
    return l.total;
}

initial {
    let mut l = new Builder();
    l.add(1);
    let n : i64 = take(l);
    let o : mut __builtin.Ownership<Builder> = new __builtin.Ownership<Builder>(l);
    let got : Builder = o.move();
    println("n " + cast<string>(n) + " size " + cast<string>(got.size()));
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
