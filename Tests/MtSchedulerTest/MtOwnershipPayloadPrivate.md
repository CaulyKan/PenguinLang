# MtOwnershipPayloadPrivate
## Description
Negative (compile): the wrapper's internal slots are private (design doc §9.3). A direct `o.__payload` read would leak the payload WITHOUT consuming the wrapper — exclusivity void; a `.__state` write would forge it. The binder rejects both on Ownership-typed receivers with an error pointing at `.move()` (the only way out) and `.is_owned()` (the only introspection).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let o : mut __builtin.Ownership<i64> = new __builtin.Ownership<i64>(7);
    let leaked : mut __builtin.Option<i64> = o.__payload;
    println("leaked");
}
```

## Compile
Args: `--enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
