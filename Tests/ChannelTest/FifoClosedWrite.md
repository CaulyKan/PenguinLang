# FifoClosedWrite
## Description
Writing onto a closed channel raises a catchable runtime error.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(2, new __builtin.FifoPolicy.backpressure());
initial {
    q.close();
    try {
        q.write(1);
        println("not reached");
    } catch (e) {
        println("caught: " + e.message);
    }
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
ExpectedStdout: EQUALS `caught: channel closed
`
ExpectedStderr: DISCARD
