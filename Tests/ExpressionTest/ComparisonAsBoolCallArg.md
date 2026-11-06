# ComparisonAsBoolCallArg
## Description
RED SENTINEL (unfixed pre-existing emitter bug, found during O1 work 2026-11-04 when the bootstrap's pass1 link died on `call void @..._types(..., i8 %t20)` with `%t20 = icmp ...` — assert the CORRECT behavior, should turn green once fixed). A comparison expression passed DIRECTLY as a `bool` call argument (`this.inner.go(it == 0)`) lowers to the raw i1 `icmp` result annotated as the parameter's `i8` — `%t4 = icmp eq i64 ...` then `call void @..._go(ptr ..., i8 %t4)` — which clang rejects ("'%t4' defined with type 'i1' but expected 'i8'", the link dies). Root cause: the call-argument emission path (LLVMCalls coerce_arg has NO i1→i8 case at all; the zext materialization only happens when the comparison is stored to a bool-typed local first — `let flag: bool = it == 0; go(flag);` links and runs fine, which is the workaround this sentinel's expected output exercises the hard way). Affects both call and call_void shapes, method and free-function callees. Expected once fixed: exit 0, `1`.
## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Inner {
    acc: mut i64;
    fun new(mut this) { this.acc = 0; }
    fun go(mut this, b: bool) {
        if (b) { this.acc = this.acc + 1; }
    }
}
class Outer {
    inner: mut Inner;
    fun new(mut this) { this.inner = new Inner(); }
    fun drive(mut this) -> i64 {
        let it: mut i64 = 0;
        while (it < 2) {
            this.inner.go(it == 0);
            it = it + 1;
        }
        return this.inner.acc;
    }
}
initial {
    let mut o = new Outer();
    println(cast<string>(o.drive()));
}
```
## Compile
Args: `--disable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedStdout: EQUALS `1
`
ExpectedExitCode: 0
