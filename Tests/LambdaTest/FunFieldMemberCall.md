# FunFieldMemberCall
## Description
Calling a function-typed FIELD through member access — `h.cb(21)` where `cb: mut fun<i32, i32>` — works on BabyPenguin (the C# reference; prints 42) and on EmperorPenguin (bind: SemanticBindExpressions fun-typed-field branch; lowering: IRGenerator RDMBR + CALL_INDIRECT). This used to be a RED SENTINEL for the EmperorPenguin gap and turned green when the field-call path landed; it now locks that path in as a regression test. The remaining function-value gaps (local fun-variable calls, lambdas, method references) are tracked by the other LambdaTest sentinels (.agents/plans/emperorpenguin-fun-values.md).

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun twice(x: i32) -> i32 { return x * 2; }
class Handler {
    cb: mut fun<i32, i32> = twice;
}
initial {
    let h = new Handler();
    println(cast<string>(h.cb(21)));
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
ExpectedStdout: EQUALS `42
`
ExpectedStderr: DISCARD
