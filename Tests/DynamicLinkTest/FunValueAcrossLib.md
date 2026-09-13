# FunValueAcrossLib
## Description
Fun values across the .penguin-lib boundary, default (direct) libmeta mode: (a) a consumer-side LAMBDA passed INTO a lib function through a `fun<i64,i64>` parameter and called there — the closure object lives in the consumer's heap, dispatch goes through the string-keyed `__FunVal` interface entry resolved inside the .so's code; (b) a lib-side lambda (capturing the factory argument) RETURNED as a `fun<i64,i64>` value and called by the consumer — the closure and its class metadata live in the .so; (c) a reference to the lib's own top-level function (`vlib3.twice`) taken by the consumer as a fun value and passed back into the lib's higher-order `apply` — the constant funval singleton + forwarding thunk are emitted into the .so. The `fun<...>` parameter/return types round-trip through the libmeta signature JSON on both paths. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace vlib3 {
    export fun twice(x: i64) -> i64 { return x * 2; }

    export fun apply(f: fun<i64, i64>, x: i64) -> i64 { return f(x); }

    export fun make_adder(n: i64) -> fun<i64, i64> {
        let f: fun<i64, i64> = fun (x: i64) -> i64 { return x + n; };
        return f;
    }
}
```
## Build 1
Kind: lib
Name: vlib3.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let add100: fun<i64, i64> = vlib3.make_adder(100);
    println("a=" + cast<string>(add100(11)));
    println("b=" + cast<string>(vlib3.apply(fun (x: i64) -> i64 { return x * 3; }, 14)));
    let t: fun<i64, i64> = vlib3.twice;
    println("c=" + cast<string>(vlib3.apply(t, 5)));
}
```
## Build 2
Args: `--lib ${WORKDIR}/vlib3.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a=111
b=42
c=10
`
ExpectedStderr: DISCARD
