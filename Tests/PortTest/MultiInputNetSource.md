# MultiInputNetSource
## Description
Variable nets feeding a MultiInput fan-in: connect(x, s.mi) registers the net's wire as a source view; the seed deliveries (10, 5) and the later net write (100, latest-wins over the 10) merge across both sources — wait#1 and wait#2 sum to 105.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there and should turn green once Phase 3 (coroutine state-machine lowering + ports) lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Sink {
    mi : mut __builtin.MultiInput<i64> = new __builtin.MultiInput<i64>();
    initial {
        let a : i64 = wait this.mi;
        let b : i64 = wait this.mi;
        println(cast<string>(a + b));
        exit(0);
    }
}

construct {
    let x : mut i64 = 10;
    let y : mut i64 = 5;
    let s : mut Sink = new Sink();
    connect(x, s.mi);
    connect(y, s.mi);
}

initial {
    x = 100;
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
ExpectedStdout: EQUALS `105
`
ExpectedStderr: DISCARD
