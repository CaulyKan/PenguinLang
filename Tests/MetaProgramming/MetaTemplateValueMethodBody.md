# MetaTemplateValueMethodBody
## Description
Value-template parameter `N` substituted inside a METHOD body, not just a field initializer. `#template(N: i32) class A { fun get_double(this) -> i64 { return N * 2; } }` — instantiating `A<5>` must specialize the method body so `N` becomes the constant 5 (`return 5 * 2` → 10). Previously value-template substitution only covered field initializers (`substitute_value_params` was applied at field-default binding only); method bodies kept the unsubstituted `N`, which then failed to resolve (N is a value template param, not a usable symbol in the method scope). This extends `substitute_value_params` to recurse into code blocks and statements (return/expression/assignment/let/if/while/for), substituting `N` -> the constant. Note: value-template params are stored as i64 (`template_value`), so the method's return type is i64 (matching value-template FUNCTION semantics, e.g. `dbl<N>() -> i64`). The field initializer `foo = N` -> 5 (i64 -> i32 field, implicit) still works too. Verified on native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
class A {
    foo: mut i32 = N;
    fun get_double(this) -> i64 {
        return N * 2;
    }
}
initial {
    let a = new A<5>();
    println("foo=" + cast<string>(a.foo) + " dbl=" + cast<string>(a.get_double()));
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
ExpectedStdout: EQUALS `foo=5 dbl=10
`
ExpectedStderr: DISCARD
