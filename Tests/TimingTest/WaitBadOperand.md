# WaitBadOperand
## Description
Negative: `wait "str";` (a string operand) has no wait semantics — not an integer (timer ticks), not bool, not IFuture/Event/port, not a call. EmperorPenguin reports a clean E_TYPE_MISMATCH at bind time (previously this fell through to a doomed do_wait member call and surfaced as E_INTERNAL "Function call has no callee symbol" at IR time). BabyPenguin still evaluates-and-discards the operand (historic passthrough), so this sentinel is EmperorPenguin-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    wait "not-a-timer";
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
