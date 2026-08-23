# BareMultiInputSink
## Description
A bare top-level MultiInput as a connect sink (connect(p.out, mi) — no member access needed): both producers' outputs register as sources and `wait mi` merges them (3 + 4 = 7).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Producer {
    output out : i64;
    v : i64;
    fun new(mut this, v: i64) { this.v = v; }
    initial { this.out.write(this.v); }
}

let mi : mut __builtin.MultiInput<i64> = new __builtin.MultiInput<i64>();

initial {
    let a : i64 = wait mi;
    let b : i64 = wait mi;
    println(cast<string>(a + b));
    exit(0);
}

construct {
    let p1 : mut Producer = new Producer(3);
    let p2 : mut Producer = new Producer(4);
    connect(p1.out, mi);
    connect(p2.out, mi);
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
ExpectedStdout: EQUALS `7
`
ExpectedStderr: DISCARD
