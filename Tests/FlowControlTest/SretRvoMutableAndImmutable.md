# SretRvoMutableAndImmutable
## Description
RVO (return-value optimization) for sret returns. Tests mutable and immutable assignment of functions returning Option<i32> via sret. Only EP Pass1 implements the correct RVO behavior.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun make(i: i32) -> Option<i32> {
        if (i > 0) { return new Option<i32>.some(i); }
        return new Option<i32>.none();
    }
    initial {
        let x: mut Option<i32> = make(5);
        x = make(20);
        let y = make(0);
        println(cast<string>(x.value_or(99)));
        println(cast<string>(y.value_or(99)));
    }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `20
99
`
ExpectedStderr: DISCARD
