# MetaStaticImplCall
## Description
RED SENTINEL (unfixed): calling a static interface-impl method through a PRIMITIVE type name (`i64.bar("5")`, where `bar` is a no-`this` method of `impl IFoo<i64> for i64`) fails in the LLVM backend with "expected instruction opcode" (same failure as static calls through a GENERIC class name like `Option<i64>.json_deserialize`). The intended behavior is `bar=5`. This blocks primitive `json_deserialize` static calls and generic Option/Box interface impls; the auto-impl works around it by reading via `JsonValue.as_*` and by NOT generating generic-class static deserialize calls. Should turn green once static impl calls through primitive/generic type names lower correctly. Requires native Pass2/Pass3.

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
