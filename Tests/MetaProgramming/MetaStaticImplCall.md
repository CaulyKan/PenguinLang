# MetaStaticImplCall
## Description
Static interface-impl method calls through a PRIMITIVE type name (`i64.bar("5")`, where `bar` is a no-`this` method of `impl IFoo<i64> for i64`) now lower correctly: the parser wraps the primitive keyword as a type-name expression, the binder resolves it to the primitive type (primitive_impl registry), and the call emits a direct call to the mangled `bar$$i64`. Intended result: `bar=5`. (Generic angle-bracket static calls like `Option<i64>.json_deserialize` still need the type already instantiated — see the json auto-impl which uses the mangled `Option__i64` form.)

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace penguin {
#template(T: type)
interface IFoo {
    fun foo(this) -> string;
    fun bar(json: string) -> mut T;
}
impl IFoo<i64> for i64 {
    fun foo(this) -> string { return "x"; }
    fun bar(json: string) -> mut i64 { return string_to_int(json); }
}
}
initial {
    let b = i64.bar("5");
    println("bar=" + cast<string>(b));
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
ExpectedStdout: EQUALS `bar=5
`
ExpectedStderr: DISCARD
