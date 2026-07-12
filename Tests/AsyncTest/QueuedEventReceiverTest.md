# QueuedEventReceiverTest
## Description
_QueuedEventReceiver polling events.

## Apply To
* BabyPenguin

## Test Code
```
    event test_event : i32;

    let eq : mut _QueuedEventReceiver<i32> = new _QueuedEventReceiver<i32>(test_event);
    initial {
        while (true) {
            let a : Option<i32> = eq.do_wait_any();
            if (a.is_some()) {
                let b : i32 = a.some;
                print(cast<string>(b));
                if (b == 2) exit(0);
            }
        }
    }
    
    initial {
        for (let i : i64 in range(0, 3)) {
            emit test_event(cast<i32>(i));
        }
    }
```

## Compile
Args: ``
Env: ``
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
