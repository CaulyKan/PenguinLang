# ClockModelRequiresMt
## Description
`--enable-clock-model` names the clock-model policy of the multi-thread scheduler (scheduler_mt.penguin). The clock model does not fundamentally depend on the mt kernel — a classic-scheduler implementation is possible in principle — but only the mt one exists today, so passing `--enable-clock-model` WITHOUT `--enable-multithread-scheduler` is a hard compile error with an explicit message (checked in main.penguin after the project-file flags pre-scan, so a `.penguins` flags=[...] is covered too). The positive combination (`--enable-multithread-scheduler --enable-clock-model`) is pinned by every other MtSchedulerTest case; this test pins the rejection.


## Apply To
* EmperorPenguin Pass3

## Test Code
```
fun main() -> i64 {
    println("never compiled");
    return 0;
}
```

## Compile
Args: `--enable-clock-model`
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: EQUALS `EmperorPenguin clock model is supported only in multithread-scheduler
`

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
