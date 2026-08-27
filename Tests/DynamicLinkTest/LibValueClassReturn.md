# LibValueClassReturn
## Description
A lib function returning a MUTABLE value-class binding (`-> mut Pair2`) called from the consumer with the result fed STRAIGHT into another call's byval argument slot — the shape that used to return a pointer to the callee's dead stack alloca (the next frame re-used those addresses and clobbered the struct tail; the trailing bool read back as garbage). Value-class returns use sret on both sides of the lib boundary now, so the result is materialized into caller-owned storage before any further call. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace vlib {
    export class Pair2 {
        a: i64 = 0;
        b: bool = false;

        fun new(mut this) {}
    }

    export fun make(a: i64, b: bool) -> mut Pair2 {
        let p: mut Pair2 = new Pair2();
        p.a = a;
        p.b = b;
        return p;
    }
}
```
## Build 1
Kind: lib
Name: vlib.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
fun describe(p: vlib.Pair2) -> string {
    return "a=" + cast<string>(p.a) + " b=" + cast<string>(p.b);
}

initial {
    println(describe(vlib.make(3, false)));
    let q: vlib.Pair2 = vlib.make(9, true);
    println(describe(q));
}
```
## Build 2
Args: `--lib ${WORKDIR}/vlib.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `a=3 b=false
a=9 b=true
`
ExpectedStderr: DISCARD
