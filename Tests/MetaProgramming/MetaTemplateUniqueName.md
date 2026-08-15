# MetaTemplateUniqueName
## Description
`IUniqueMangleName` interface (core_builtin) — `fun get_unique_name(this) -> string` — the contract for a canonical, deterministic name of a non-scalar value-template argument, used to mangle/dedup object value args (`Foo<#make_list(...)>`) by value rather than address. This test exercises the contract in isolation (the object-value-arg flow that consumes it is a later brick): a user class implements `IUniqueMangleName`, is cast to the interface, and the virtual call returns a value-derived name. Apply To is EmperorPenguin-only (BabyPenguin has its own builtins; the object ABI is native-JIT-only). Verified on native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class C {
    n: i64;
    fun new(mut this, n: i64) {
        this.n = n;
    }
    impl IUniqueMangleName {
        fun get_unique_name(this) -> string {
            return "C[" + cast<string>(this.n) + "]";
        }
    }
}
initial {
    let c = new C(7);
    let i: IUniqueMangleName = cast<IUniqueMangleName>(c);
    println(i.get_unique_name());
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
ExpectedStdout: EQUALS `C[7]
`
ExpectedStderr: DISCARD
