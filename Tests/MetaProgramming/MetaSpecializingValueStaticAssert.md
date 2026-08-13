# MetaSpecializingValueStaticAssert
## Description
`#specializing`'s `#error(...)` acts as a compile-time static_assert: instantiating `foo<1>` reaches the `#error("N never 1")` branch of the callback, which fails compilation. This is the positive, declarative alternative to Rust's `where` clauses / C++ SFINAE — the condition is just meta code run at instantiation. Compile must fail (NONZERO exit code), so there is no `## Run` section.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace penguin {
#template(T: type)
interface IDescribe {
    fun describe(this) -> string;
}

#template(N: i32)
class foo {
    impl IReferenceType;
}

#specializing foo<N> {
    if (N > 3) {
        impl IDescribe {
            fun describe(this) -> string { return "big"; }
        }
    }
    else if (N == 2) {
        impl IDescribe {
            fun describe(this) -> string { return "two"; }
        }
    }
    else if (N == 1) {
        #error("N never 1");
    }
    else {
    }
}
}
initial {
    let a = new penguin.foo<1>();
    let da: penguin.IDescribe = a;
    println("n1=" + da.describe());
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
