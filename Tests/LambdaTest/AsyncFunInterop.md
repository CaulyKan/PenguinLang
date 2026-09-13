# AsyncFunInterop
## Description
An `async fun` declaration referenced as a value: same-flag binding (async_fun var) and BOTH cross-flag directions (async fun -> fun var, fun var -> async_fun var). fun/async_fun with equal signatures are interchangeable — BabyPenguin parity (shared layout, async flag affects spawn syntax only, not the call ABI). Direct value calls run inline with implicit-wait semantics.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    async fun g() -> i32 {
        wait;
        return 7;
    }
    initial {
        let h : async_fun<i32> = g;
        println(cast<string>(h()));
        let f : fun<i32> = g;
        println(cast<string>(f()));
        let k : async_fun<i32> = f;
        println(cast<string>(k()));
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
ExpectedStdout: EQUALS `7
7
7
`
ExpectedStderr: DISCARD
