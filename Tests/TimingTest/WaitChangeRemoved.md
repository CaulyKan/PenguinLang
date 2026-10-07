# WaitChangeRemoved
## Description
`wait change(x)` (edge-detection sugar) is REMOVED in the concurrency redesign (M0): the watched expression has no wake source — its writer cannot know who watches it. Both compilers must REJECT it at compile time with E_TYPE_MISMATCH ("wait change(x) is removed: ... wait on an Event, a channel, a future or a port transaction instead"). Wait on an Event, a channel, a future or a port transaction instead. Locked as a red sentinel for the removal itself: this test must stay a compile failure forever.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let x : mut i64 = 5;

initial {
    let v : i64 = wait change(x);
    println(cast<string>(v));
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
