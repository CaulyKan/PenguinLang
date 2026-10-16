# StringConcatMixedTypes

## Description
`+` whose result is a string concatenates and stringifies the OTHER operand by
its own type — integers, bool, f64, whether the operand is a literal, a
variable or a call result. BabyPenguin's VM does this at runtime (its
EvalBinOp StringValue fast path); EmperorPenguin used to pass the raw i64/f64
register straight as the ptr argument of `_emperor_string_concat` — invalid IR
("`%t1' defined with type 'i64' but expected 'ptr'`"), a link error on every
mixed concat. Fixed in `LLVMEmitter.emit_binop`: each non-string operand is
coerced through the same to-string runtime arms `emit_cast` uses (sext for
narrow signed ints, fpext for f32). Note `string + char` is a semantic
E_TYPE_MISMATCH on both compilers and is intentionally out of scope here.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun f() -> i64 { return 42; }

initial {
    let v : i64 = 7;
    let n : i32 = -5;
    let u : u8 = 200;
    let b : bool = true;
    let d : f64 = 42.5;
    println("lit:" + 42);
    println("var:" + v);
    println("call:" + f());
    println("i32:" + n);
    println("u8:" + u);
    println("bool:" + b);
    println("f64:" + d);
    println("chain:" + 1 + "-" + 2);
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
ExpectedStdout: EQUALS `lit:42
var:7
call:42
i32:-5
u8:200
bool:true
f64:42.5
chain:1-2
`
ExpectedStderr: DISCARD
