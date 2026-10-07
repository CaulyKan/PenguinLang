# FifoBackpressure
## Description
Capacity-2 Fifo with 5 writes: the producer suspends when the channel is full (write parks on bare wait until a slot frees) and resumes as the consumer drains — Go-channel flow control with zero flow-control code in the module bodies.

UPDATED (M0 BabyPenguin scheduler alignment): backpressure is a CONDITION wait now (no bare-wait polling), so the writer drains a freed slot and the next write lands in the SAME delta — writes and reads batch in pairs instead of classic's write-at-a-time pattern (13.2(1) predicted exactly this: both interleavings are legal). Verified byte-identical on BabyPenguin, pass1+mt and pass3+mt.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
Args: `--enable-coroutine --enable-multithread-scheduler`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `w 1
w 2
r 1
r 2
w 3
w 4
r 3
r 4
w 5
producer done
r 5
consumer done
`
ExpectedStderr: DISCARD
