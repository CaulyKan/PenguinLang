# AsyncIndirectSpawn
## Description
`async f(args)` spawning through a fun VALUE (local fun variable holding a function reference): the callable runs on the spawned coroutine, the future delivers its result. BabyPenguin reference behavior; EmperorPenguin's spawn desugar captures the callable into the ctx class's `__fn` field and calls through it in __enter.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun compute(v: i32) -> i32 {
        println("compute");
        return v * 3;
    }
    initial {
        let f : fun<i32, i32> = compute;
        let a : IFuture<i32> = async f(5);
        println("before");
        let r : i32 = wait a;
        println(cast<string>(r));
        println("after");
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
ExpectedStdout: EQUALS `before
compute
15
after
`
ExpectedStderr: DISCARD
