# LiteralFirstArithReturn
## Description
RED SENTINEL (known gap in EmperorPenguin, found during fun-values work): an integer literal as the FIRST operand of an arithmetic expression (`1 + b` where b: i32) types the whole expression as i64 on EmperorPenguin (the i64 literal wins the binary promotion), so `return 1+b;` in a function returning i32 fails with E_RETURN_TYPE_MISMATCH. The mirrored form `b + 1` compiles fine (the literal is re-typed to the variable's type), and BabyPenguin accepts both — integer literals should coerce to the other operand's type in either position. Minimal repro verified on pass1: `fun call(b: i32) -> i32 { return 1+b; }` fails, `return b+1;` compiles. Should turn green once literal-first binary operands are re-typed like literal-second ones.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun calc(b : i32) -> i32 {
    return 1+b;
}
initial {
    println(cast<string>(calc(2)));
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
ExpectedStdout: EQUALS `3
`
ExpectedStderr: DISCARD
