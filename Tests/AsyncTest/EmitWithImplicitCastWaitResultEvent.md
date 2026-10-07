# EmitWithImplicitCastWaitResultEvent
## Description
Three emits with no explicit wait between them still deliver one value each: `emit` yields a delta after broadcasting, so the parked waiter consumes every transaction (a wait slot holds one value — the yield is what keeps back-to-back emits from collapsing).

REVISED (M0 BabyPenguin scheduler alignment): emits separated by bare `wait;` — back-to-back emits collapse into the single receiver's subscription slot under the new semantics (only the last value would be observed). With pacing the output is identical on the classic and new schedulers (verified on BabyPenguin, pass3-classic and pass3-mt).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let test_event : mut __builtin.Event<i32> = new __builtin.Event<i32>();

initial {
    let a : i32 = wait test_event;
    print(cast<string>(a));
    let b : i32 = wait test_event;
    print(cast<string>(b));
    let c : i32 = wait test_event;
    print(cast<string>(c));
}

initial {
    test_event.emit(cast<i32>(0));
    wait;
    test_event.emit(cast<i32>(1));
    wait;
    test_event.emit(cast<i32>(2));
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `012`
ExpectedStderr: DISCARD
