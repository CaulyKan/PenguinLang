# WaitConditionRemoved
## Description
`wait <condition>` (level-sensitive wait on a bool operand) is REMOVED in the concurrency redesign (M0): a bool operand has no wake source — the writer of the condition cannot know who waits on it. Both compilers must REJECT it at compile time with E_TYPE_MISMATCH ("wait <condition> is removed: ... wait on an Event, a channel, a future or a port transaction instead"). Wait on an Event, a channel, a future or a port transaction instead. Locked as a red sentinel for the removal itself: this test must stay a compile failure forever.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let a : mut i64 = 0;

initial {
    wait a == 5;
    println("unreachable");
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
