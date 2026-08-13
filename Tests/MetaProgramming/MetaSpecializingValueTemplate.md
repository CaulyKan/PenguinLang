# MetaSpecializingValueTemplate
## Description
`#specializing` with a VALUE template param (req5 style): `#template(N: i32) class foo` + `#specializing foo<N>` dispatches on N — `N > 3` activates one impl, `N == 2` a different one, `N == 1` fires a static-assert `#error`, `else` activates nothing. The block is compiled into a meta (#fun) callback run when `foo<N>` is instantiated; each executed `impl` statement registers that interface impl on the specialized class. Verified on native Pass2/Pass3 (meta JIT).

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
    let a = new penguin.foo<5>();
    let da: penguin.IDescribe = a;
    println("n5=" + da.describe());
    let b = new penguin.foo<2>();
    let db: penguin.IDescribe = b;
    println("n2=" + db.describe());
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
ExpectedStdout: EQUALS `n5=big
n2=two
`
ExpectedStderr: DISCARD
