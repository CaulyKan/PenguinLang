# HexBinaryOctalLiterals
## Description
Integer literals in hex (0x/0X), binary (0b/0B) and octal (leading-0) spellings normalize to their decimal value at PARSE time, in every context (typed let, inferred let, arithmetic operands). Previously the grammar accepted these spellings but no semantic layer implemented them: BabyPenguin's `ResolveLiteralType` (decimal TryParse) failed with E_RESOLVE_TYPE, and EmperorPenguin mis-bound hex digits 'e'/'E' as a FLOAT literal shape (`0xE` → f64) and emitted raw `0x…` text into LLVM IR where it is float syntax (`add i32 0, 0xFF` — invalid IR) and a leading-0 octal like `017` silently parsed as decimal 17. The fix normalizes token text to decimal in both parsers (PenguinLangParser `PrimaryExpression.NormalizeIntegerLiteral`, EmperorPenguin `normalize_int_literal`), so bind/VM/emitter paths all see plain decimals.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a: u32 = 0xFF;
    let b: u32 = 0XE;
    let c: u32 = 0b1010;
    let d: u32 = 0B110;
    let e: u32 = 017;
    let f: u32 = 0xDEADBEEF;
    println("a=" + cast<string>(a));
    println("b=" + cast<string>(b));
    println("c=" + cast<string>(c));
    println("d=" + cast<string>(d));
    println("e=" + cast<string>(e));
    println("f=" + cast<string>(cast<i64>(f)));
    let h: u32 = 0x00000001;
    println("h=" + cast<string>(h));
    let radix_mix: i64 = cast<i64>(0xFF) + cast<i64>(0b1);
    println("mix=" + cast<string>(radix_mix));
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
ExpectedStdout: EQUALS `a=255
b=14
c=10
d=6
e=15
f=3735928559
h=1
mix=256
`
ExpectedStderr: DISCARD
