# WaitCondition
## Description
The condition-wait idiom migrated to the event form: `wait <condition>` is REMOVED (a bool operand has no wake source — the writer of the condition cannot know who waits on it; concurrency redesign M0). The setter prints, emits on an Event; the waiter parks on the event and prints when the value arrives — the same observable ordering the level-sensitive form had.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let ev : mut __builtin.Event<i64> = new __builtin.Event<i64>();

initial {
    wait 2;
    println("set");
    ev.emit(5);
}

initial {
    let v : i64 = wait ev;
    println("seen");
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `set
seen
`
ExpectedStderr: DISCARD
