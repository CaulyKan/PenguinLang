# MetaTypeCallGenericArg
## Description
req2 (meta x template): a type-returning `#fun()` meta call used as a GENERIC TYPE ARGUMENT — `Box<#pick_type(0)>`. The parser (`parse_typeSpecifierInGeneric`) now accepts a `#fun()` type spec inside generic angle brackets (mirroring the top-level `parse_typeSpecifier` handling), and the existing resolution path JIT-splices the returned type: `#pick_type(0)` returns `#typeof(i32)`, so `Box<#pick_type(0)>` resolves to `Box<i32>` and the `new Box<i32>(7)` initializer type-checks only because of that. A failure to parse or resolve the meta-call generic arg fails compilation. Requires native Pass2/Pass3 (meta JIT).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun pick_type(which: i64) -> type {
    if (which == 0) { return #typeof(i32); }
    return #typeof(i64);
}
#template(T: type)
class Box {
    value: mut T;
    fun new(mut this, v: T) { this.value = v; }
}
initial {
    let b: Box<#pick_type(0)> = new Box<i32>(7);
    println("v=" + cast<string>(b.value));
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
ExpectedStdout: EQUALS `v=7
`
ExpectedStderr: DISCARD
