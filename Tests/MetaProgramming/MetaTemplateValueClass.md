# MetaTemplateValueClass
## Description
req5 (meta x template): a non-type (value) template parameter on a CLASS, specialized by substitution. `#template<N:i32> class A { foo: mut i32 = N; bar: mut i32 = N + 1; }` — instantiating `A<5>` specializes to `A__5` with the value param `N` substituted by the constant 5 in the field initializers (foo=5, bar=6) before binding. The value arg rides the generic-args plumbing as a synthetic BoundType (mangles A__5); specialize_class_def records value-param names+values and substitutes N->constant in member expressions. Verified on native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
class A {
    foo: mut i32 = N;
    bar: mut i32 = N + 1;
}
initial {
    let a = new A<5>();
    println("foo=" + cast<string>(a.foo) + " bar=" + cast<string>(a.bar));
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
ExpectedStdout: EQUALS `foo=5 bar=6
`
ExpectedStderr: DISCARD
