# FunValueAcrossLibField
## Description
Fun-typed FIELDS and GLOBALS across the .penguin-lib boundary (direct libmeta): the lib exports `Hook` — a class with a `fun<i64,i64>` field and a `run` method that calls the field through the receiver — plus a fun-typed GLOBAL initialized to a lib-side lambda. The consumer (a) `new`s the lib class (allocation compiled into the consumer), stores a CONSUMER lambda into the fun field and calls the lib's `run`, which dispatches indirectly on the consumer-made closure; (b) calls the fun-typed global, whose initializer text is re-bound consumer-side (a fresh consumer-side closure over nothing) while the global's storage interposes through the GOT. Pass3-only.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace vlib4 {
    export class Hook {
        impl IReferenceType;
        base: i64 = 0;
        f: mut fun<i64, i64>;

        fun new(mut this, base: i64) { this.base = base; }

        fun run(mut this, x: i64) -> i64 { return this.f(this.base + x); }
    }

    export let g_hook: fun<i64, i64> = fun (x: i64) -> i64 { return x - 1; };
}
```
## Build 1
Kind: lib
Name: vlib4.penguin-lib
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let h: mut vlib4.Hook = new vlib4.Hook(1000);
    h.f = fun (x: i64) -> i64 { return x * 2; };
    println("field=" + cast<string>(h.run(21)));
    println("global=" + cast<string>(vlib4.g_hook(50)));
}
```
## Build 2
Args: `--lib ${WORKDIR}/vlib4.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `field=2042
global=49
`
ExpectedStderr: DISCARD
