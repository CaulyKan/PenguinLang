# MultiInputConnect
## Description
MultiInput fan-in: two output ports connect into one `MultiInput<i64>` field (two separate connects). `wait mi` returns transactions across ANY source, in write order.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): MultiInput (dynamic fan-in) exists only in BabyPenguin's builtin so far — EmperorPenguin fails here and should turn green once the ports layer lands there (Phase 3). BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Producer {
    output out : i64;
    value : i64;

    fun new(mut this, v: i64) {
        this.value = v;
    }

    initial {
        this.out.write(this.value);
    }
}

class Sink {
    inputs : mut __builtin.MultiInput<i64> = new __builtin.MultiInput<i64>();

    initial {
        while (true) {
            let v : i64 = wait this.inputs;
            println(cast<string>(v));
        }
    }
}

construct {
    let p1 : mut Producer = new Producer(1);
    let p2 : mut Producer = new Producer(2);
    let s : mut Sink = new Sink();
    connect(p1.out, s.inputs);
    connect(p2.out, s.inputs);
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1
2
`
ExpectedStderr: DISCARD
