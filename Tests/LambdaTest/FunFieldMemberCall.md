# FunFieldMemberCall
## Description
RED SENTINEL (known gap in EmperorPenguin): calling a function-typed FIELD through member access — `h.cb(21)` where `cb: mut fun<i32, i32>` — works on BabyPenguin (the C# reference; prints 42) but EmperorPenguin does not support it: bind_function_call's member-access callee path only resolves function symbols, and a variable (field) symbol falls through to the void-typed no-callee fallback, which dies in the IR generator (`E_INTERNAL: Symbol register not found`). BabyPenguin's local fun-variable calls (`x(1,2)`) already work on EmperorPenguin — only the field-flavored form is missing. Should turn green once bind_function_call invokes a fun-typed field symbol (or lowers it to a fat-pointer call).

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
