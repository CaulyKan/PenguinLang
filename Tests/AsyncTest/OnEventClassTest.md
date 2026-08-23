# OnEventClassTest
## Description
Event as a first-class class field: an in-class initial and an external initial both wait on the same instance's event object — one emit reaches both parked waiters (broadcast, one output per handler per value).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): the event/emit/on keywords were removed in favor of the first-class Event<T> broadcast class; EmperorPenguin still ships the old receiver-based Event and has no emit method, so it fails here and should turn green once its stdlib catches up (Phase 3). BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Foo {
    test_event : mut __builtin.Event<i32> = new __builtin.Event<i32>();

    initial {
        while (true) {
            let b : i32 = wait this.test_event;
            print(cast<string>(b));
        }
    }

    fun foo(this: Foo) {
        this.test_event.emit(cast<i32>(1));
        this.test_event.emit(cast<i32>(2));
    }
}

let f : Foo = new Foo();

initial {
    while (true) {
        let b : i32 = wait f.test_event;
        print(cast<string>(b));
    }
}

initial {
    f.foo();
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
ExpectedStdout: EQUALS `1122`
ExpectedStderr: DISCARD
