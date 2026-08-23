# CatchPanic
## Description
Basic try/catch: `panic()` inside the try body prints its message, then raises a catchable `__builtin.RuntimeError`; the catch clause binds it to `e` (fields `message`/`code`), execution continues after the try statement.

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): language-level try/catch is not implemented in EmperorPenguin yet — it fails at parse there and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
fun danger() {
    panic("boom");
}

initial {
    try {
        danger();
        println("not reached");
    } catch (e) {
        println("caught");
        println(e.message);
        println(cast<string>(e.code));
    }
    println("after");
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `panic: boom
caught
boom
1
after
`
ExpectedStderr: DISCARD
