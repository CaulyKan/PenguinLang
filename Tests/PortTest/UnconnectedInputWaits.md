# UnconnectedInputWaits
## Description
Waiting an input that is never connected parks the routine gracefully (design: an unbound input wait blocks, it does not crash) — the input field carries a permanently idle source, the waiter contributes identical rounds and the program ends at quiescence. The module is instantiated DYNAMICALLY (new inside an initial, outside any construct): statically-visible topology (construct blocks) now rejects unconnected default-less inputs at compile time (see ChallengeTest/StaticCheckUnconnectedInput), so the graceful runtime park is the dynamic-instantiation path.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the RTL ports/connect feature set is not implemented in EmperorPenguin yet — it fails there at parse and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class M {
    input x : i64;
    initial {
        let v : i64 = wait this.x;
        println("never");
    }
}

initial {
    let m : mut M = new M();
    println("start");
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
ExpectedStdout: EQUALS `start
`
ExpectedStderr: DISCARD
