# MetaTemplateMetaCallValueArg
## Description
A generic argument written as a compile-time #fun() CALL whose declared return is a VALUE — `A<#compute_n()>` with `#fun compute_n() -> i64`. try_eval_meta_value_arg discriminates by the DECLARED return type (`type` stays on the req2 type path), JIT-evaluates the call, and wraps the i64 result as an int value arg; the template then specializes with N=5 (mangled A__5, field initializer `foo: i32 = N` substitutes to 5). This makes value-template args arbitrary compile-time expressions, not just integer literals. Apply To is Pass3 ONLY: evaluating a meta-call arg requires the compile-time JIT (meta_runtime_available), which the pass2 bootstrap build does not have (stub config — same reason M4 routing is pass3+); literal value args still work on pass2 via the fixpoint. Verified on native Pass3.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun compute_n() -> i64 {
    return 5;
}
#template(N: i32)
class A {
    foo: i32 = N;
}
initial {
    let a = new A<#compute_n()>();
    println("foo=" + cast<string>(a.foo));
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
ExpectedStdout: EQUALS `foo=5
`
ExpectedStderr: DISCARD
