# MetaTemplateValueEnum
## Description
A non-type (value) template parameter on an ENUM. `#template(N: i32) enum E` — `new E<5>.a()` specializes to `E__5`, whose method bodies substitute `N` → the constant 5 (`fun get(this) -> i64 { return N; }` → `return 5`, output `n=5`). Previously value-template substitution only covered class field initializers; enum value templates (this test) and interface value templates (MetaTemplateValueInterface) are the M3 new capabilities. Verified on native Pass2/Pass3 (runtime specialization via the fixpoint).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
enum E {
    a;
    fun get(this) -> i64 {
        return N;
    }
}
initial {
    let e = new E<5>.a();
    println("n=" + cast<string>(e.get()));
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
