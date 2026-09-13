# FunValueAcrossLibText
## Description
Fun values across the .penguin-lib boundary in `--libmeta=text` mode (parity with FunValueAcrossLib's direct mode): the libmeta serializer writes `fun<...>` parameter/return types as `{"k":"fun","a":[ret,params...]}` and the text materializer must spell them back as `fun<ret, params...>` source syntax (arg 0 = return type). A consumer lambda passed in, a lib lambda returned out, and a lib function reference passed back all through the materialized declaration text. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace vlib5 {
    export fun thrice(x: i64) -> i64 { return x * 3; }

    export fun apply(f: fun<i64, i64>, x: i64) -> i64 { return f(x); }

    export fun make_mul(n: i64) -> fun<i64, i64> {
        let f: fun<i64, i64> = fun (x: i64) -> i64 { return x * n; };
        return f;
    }
}
```
## Build 1
Kind: lib
Name: vlib5.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let mul6: fun<i64, i64> = vlib5.make_mul(6);
    println("a=" + cast<string>(mul6(7)));
    println("b=" + cast<string>(vlib5.apply(fun (x: i64) -> i64 { return x + 9; }, 1)));
    let t: fun<i64, i64> = vlib5.thrice;
    println("c=" + cast<string>(vlib5.apply(t, 4)));
}
```
## Build 2
Args: `--lib ${WORKDIR}/vlib5.penguin-lib --libmeta=text`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a=42
b=10
c=12
`
ExpectedStderr: DISCARD
