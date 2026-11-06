# UntypedFieldInferredFromInitializer
## Description
Untyped class fields (`num = 7;`) are inferred from their initializer. The BabyPenguin reference has always done this (Constructor pass InferVariableType); EmperorPenguin previously left the field at the default void type — the field silently vanished from the class layout ([0 x i32] field offsets) and member reads lowered to a literal `void` (clang "expected value token" at link). Fix (pass 2 resolve_class_pair): literal and non-generic new-expression initializers infer the field type (immutable-default mutability, matching the typed-field form); a write through a `mut` instance stays legal (field mutability follows the instance). Correct behavior asserted: the default `7` and the overwritten `9` both read back, exit 0.
## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3
## Test Code
```
class Cfg {
    num = 7;
    label = "unset";
}

initial {
    let c: mut Cfg = new Cfg();
    let a: i64 = c.num;
    c.num = 9;
    println("a=" + cast<string>(a) + " b=" + cast<string>(c.num) + " label=" + c.label);
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
ExpectedStdout: EQUALS `a=7 b=9 label=unset
`
ExpectedStderr: DISCARD
