# AsyncLambda
## Description
`async_fun (...) -> R { ... }` lambda expression bound to an async_fun variable (suspends inside, called inline with implicit-wait semantics), alongside a capturing plain lambda for contrast. BabyPenguin reference behavior; EmperorPenguin synthesizes an is_funval closure class whose __call carries the async flag.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let m : async_fun<i32> = async_fun () -> i32 {
            wait;
            return 9;
        };
        println(cast<string>(m()));
        let n : i32 = 5;
        let p : fun<i32> = fun () -> i32 { return n * 2; };
        println(cast<string>(p()));
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
ExpectedStdout: EQUALS `9
10
`
ExpectedStderr: DISCARD
