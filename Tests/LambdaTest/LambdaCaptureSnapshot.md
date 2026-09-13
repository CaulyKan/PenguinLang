# LambdaCaptureSnapshot
## Description
Captures are BY-VALUE snapshots taken when the lambda expression is evaluated: modifying the captured variable afterwards does not change what the closure sees. (Mutating the snapshot inside the lambda is intentionally NOT tested here — it writes the closure's own field.)
BabyPenguin reference behavior; EmperorPenguin implements the same semantics (capture fields copied through the synthesized constructor).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let x : mut i32 = 1;
        let f : fun<i32> = fun () -> i32 { return x; };
        x = 100;
        println(cast<string>(f()));
        let y : mut i32 = 2;
        let g : fun<i32> = fun () -> i32 { y = y + 10; return y; };
        println(cast<string>(g()));
        println(cast<string>(g()));
        println(cast<string>(y));
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
ExpectedStdout: EQUALS `1
12
22
2
`
ExpectedStderr: DISCARD
