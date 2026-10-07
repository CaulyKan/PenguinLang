# AsyncEventReceiverTest
## Description
Two independent wait loops on one event: every emitted value is delivered to BOTH parked waiters (broadcast — each `wait` owns its delivery slot). Within one delta the waiters wake in spawn order, so the second one ends the program after both have printed.

REVISED (M0 BabyPenguin scheduler alignment): emits separated by bare `wait;` — without pacing, three back-to-back emits collapse and one receiver's exit(0) fires before the other observes the last value. With pacing the classic interleaving A0B0A1B1A2B2 is reproduced exactly on the aligned BabyPenguin scheduler (verified) and on pass3-classic; NOTE pass3-mt wakes the two receivers in the opposite intra-delta order (B first) — intra-delta order is explicitly NOT a language guarantee (design 1.3), which is why this test runs the EP backends on the classic scheduler.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let test_event : mut __builtin.Event<i32> = new __builtin.Event<i32>();

initial {
    while (true) {
        let b : i32 = wait test_event;
        print("A");
        print(cast<string>(b));
    }
}

initial {
    while (true) {
        let b : i32 = wait test_event;
        print("B");
        print(cast<string>(b));
        if (b == 2) exit(0);
    }
}

initial {
    for (let i : i64 in range(0, 3)) {
        test_event.emit(cast<i32>(i));
        wait;
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
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `A0B0A1B1A2B2`
ExpectedStderr: DISCARD
