# QuiescentExit
## Description
All initial routines complete after their timers fire; the scheduler quiesces and the program exits with code 0.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    wait 1 tick;
    println("A");
}
initial {
    wait 2 tick;
    println("B");
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `A
B
`
ExpectedStderr: DISCARD