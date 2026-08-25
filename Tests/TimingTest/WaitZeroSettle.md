# WaitZeroSettle
## Description
An async function assigned to a global variable; waiting 0 ticks settles it, so the initial routine reads the new value.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
let x : mut i64 = 0;
async fun set_x() {
    x = 42;
}
initial {
    let f = async set_x();
    wait 0 tick;
    println(cast<string>(x));
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
ExpectedStdout: EQUALS `42
`
ExpectedStderr: DISCARD