# FifoBackpressure
## Description
Capacity-2 Fifo with 5 writes: the producer suspends when the channel is full (write parks on bare wait until a slot frees) and resumes as the consumer drains — Go-channel flow control with zero flow-control code in the module bodies.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the channel layer (Fifo/LatestChannel/MergeChannel + close semantics) is BabyPenguin-only so far — EmperorPenguin fails here and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
let q : mut __builtin.Fifo<i64> = new __builtin.Fifo<i64>(2, new __builtin.FifoPolicy.backpressure());
initial {
    let i : mut i64 = 1;
    while (i <= 5) {
        q.write(i);
        println("w " + cast<string>(i));
        i += 1;
    }
    println("producer done");
}
initial {
    let n : mut i64 = 0;
    while (n < 5) {
        let v : i64 = wait q;
        println("r " + cast<string>(v));
        n += 1;
    }
    println("consumer done");
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `w 1
w 2
r 1
w 3
r 2
w 4
r 3
w 5
producer done
r 4
r 5
consumer done
`
ExpectedStderr: DISCARD
