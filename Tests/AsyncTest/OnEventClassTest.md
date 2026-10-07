# OnEventClassTest
## Description
Event as a first-class class field: an in-class initial and an external initial both wait on the same instance's event object — one emit reaches both parked waiters (broadcast, one output per handler per value).

REVISED (M0 BabyPenguin scheduler alignment): emits are separated by a bare `wait;` and a watchdog exits — back-to-back emits COLLAPSE into a waiter's subscription slot under the new semantics (one delivery per delta), and the two receiver loops park forever once the emitter drains. With the pacing the observable interleaving is identical on the classic and new schedulers (verified on BabyPenguin, pass3-classic and pass3-mt).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

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
        wait;
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

initial {
    wait 5;
    exit(0);
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
ExpectedStdout: EQUALS `1122`
ExpectedStderr: DISCARD
