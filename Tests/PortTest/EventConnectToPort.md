# EventConnectToPort
## Description
An Event wired into input ports (connect(ev, f.x)): every emit feeds each connected input through its own permanent wire (independent cursor) AND still reaches parked wait-subscribers — one emit(20) wakes the direct `wait ev` waiter (prints 20) and both echo modules (20 -> 21 each -> 231).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Echo {
    input x : i64;
    output y : i64;
    initial {
        while (true) {
            let v : i64 = wait this.x;
            this.y.write(v + 1);
        }
    }
}

let ev : mut __builtin.Event<i64> = new __builtin.Event<i64>();

construct {
    let e1 : mut Echo = new Echo();
    let e2 : mut Echo = new Echo();
    connect(ev, e1.x);
    connect(ev, e2.x);
}

initial {
    let a : i64 = wait e1.y;
    let b : i64 = wait e2.y;
    println(cast<string>(a * 10 + b));
    exit(0);
}

initial {
    let raw : i64 = wait ev;
    println(cast<string>(raw));
}

initial {
    ev.emit(20);
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
ExpectedStdout: EQUALS `20
231
`
ExpectedStderr: DISCARD
