# MetaTemplateValueMetaCall
## Description
req4 (meta x template): a non-type (value) template param `N` is passed to a `#fun` meta function. `#template<N:i32> fun foo() { return #test(N); }` is specialized at runtime (D6): `foo<5>()` → `foo__5`, whose body substitutes `N` → `5` giving `return #test(5);`. The `#test(5)` meta call is JIT-evaluated at compile time and spliced as the constant 10, so `foo__5()` returns 10 at runtime. Requires native Pass2/Pass3 (meta JIT for the `#test` splice).

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
