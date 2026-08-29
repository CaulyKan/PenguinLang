# WaitCondition
## Description
`wait <condition>` is a level-sensitive wait: the routine parks and the condition is re-evaluated every scheduler round until it holds (replaces the old `on <expr>` routines).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let a : mut i32 = 0;

initial {
    wait 2 tick;
    a = 5;
    println("set");
}

initial {
    wait a == 5;
    println("seen");
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `set
seen
`
ExpectedStderr: DISCARD
