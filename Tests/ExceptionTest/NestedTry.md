# NestedTry
## Description
Nested try/catch: the inner handler catches first; an error raised inside a catch body is caught by the enclosing try's handler (innermost-wins dispatch).

RED SENTINEL on EmperorPenguin Pass1 (in Apply To): language-level try/catch is not implemented in EmperorPenguin yet — it fails at parse there and should turn green once Phase 3 lands. BabyPenguin is the reference.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
fun deep() {
    panic("inner");
}

initial {
    try {
        try {
            deep();
        } catch (e) {
            println("inner caught: " + e.message);
            panic("from-catch");
        }
    } catch (e) {
        println("outer caught: " + e.message);
    }
    println("done");
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
ExpectedStdout: EQUALS `panic: inner
inner caught: inner
panic: from-catch
outer caught: from-catch
done
`
ExpectedStderr: DISCARD
