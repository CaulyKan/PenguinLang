# MetaTemplateValueMetaCall
## Description
req4 (meta x template): a non-type (value) template param `N` is passed to a `#fun` meta function. `#template<N:i32> fun foo() { return #test(N); }` desugars to `#fun foo(N:i64) { return #test(N); }`; inside foo's #fun body the `#test(N)` meta call rewrites to a plain `test(N)` call, and `test` is synthesized into unit B (alongside foo), so the compile-time evaluation resolves: `foo<5>()` JIT-evaluates foo(5) -> test(5) -> 10. Requires native Pass2/Pass3 (meta JIT).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun test(i: i64) -> i64 { return i * 2; }
#template(N: i32)
fun foo() -> i64 {
    return #test(N);
}
initial {
    let r = foo<5>();
    println("r=" + cast<string>(r));
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
ExpectedStdout: EQUALS `r=10
`
ExpectedStderr: DISCARD
