# KeywordAsParameterName
## Description
Keywords silently accepted as NAMES parse the definition but every USE site lexes as the keyword and produces an EMPTY expression (the hole behind the resolved AsyncSpawnKeywordName sentinel: `fun pure(...)` — and the same-trait `initial` constructor-parameter bug found while building Signal<T>). The parser now rejects the reserved set (pure/!pure/extern/export/async/!async/initial/wait/tick) at the parameter, let-variable and class-field name positions with a located E_PARSE. Type-name keywords (double, float, ...) stay legal names — their call sites resolve (de-facto language; DynamicLinkTest depends on `fun double`).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
fun f(initial: i64) -> i64 {
    return 1;
}

class C {
    wait: mut i64;
}

initial {
    let tick : mut i64 = 3;
    println(cast<string>(f(1)) + cast<string>(tick));
}
```

## Compile
Args: `--enable-coroutine`
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
