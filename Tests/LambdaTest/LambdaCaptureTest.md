# LambdaCaptureTest
## Description
Lambda capturing an enclosing function's parameter AND local, returned as a fun value and called outside the defining frame (the closure outlives it).
BabyPenguin reference behavior; EmperorPenguin implements the same by-value snapshot closure semantics (synthesized `__lambda_<n>` class).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun adder(a: i32) -> fun<i32, i32> {
        let base : i32 = a * 10;
        let f : fun<i32, i32> = fun (x : i32) -> i32 { return x + base; };
        return f;
    }
    initial {
        let g : fun<i32, i32> = adder(3);
        println(cast<string>(g(5)));
        let h : fun<i32, i32> = adder(7);
        println(cast<string>(h(5)));
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
ExpectedStdout: EQUALS `35
75
`
ExpectedStderr: DISCARD
