# MetaTemplateValueInterface
## Description
A non-type (value) template parameter on an INTERFACE. `#template(N: i32) interface I` — `impl I<5>` specializes to `I__5`, whose default-method body substitutes `N` → the constant 5 (`fun get(this) -> i64 { return N; }` → `return 5`). A class implements `I<5>`, is cast to `I<5>`, and calls `get()` — output `n=5`. Previously value-template substitution only covered class field initializers; interface value templates (this test) and enum value templates (MetaTemplateValueEnum) are the M3 new capabilities. Verified on native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
interface I {
    fun get(this) -> i64 {
        return N;
    }
}
class C {
    impl I<5> {
        fun get(this) -> i64 {
            return 5;
        }
    }
}
initial {
    let c = new C();
    let i: I<5> = cast<I<5>>(c);
    println("n=" + cast<string>(i.get()));
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
ExpectedStdout: EQUALS `n=5
`
ExpectedStderr: DISCARD
