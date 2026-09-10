# StringConstantFold
## Description
Locks in compile-time folding of string-literal `+` chains (SemanticBindExpressions.fold_string_literal_concats): adjacent string literals joined by `+` merge into ONE literal at bind time, so the folded text becomes a single static `.rodata` constant (`@str_N`, see StringHeaderOps) instead of a runtime `_emperor_string_concat` heap allocation per evaluation. Covers: escaped content folded verbatim (`"a\n" + "b\tc"` keeps real newline/tab), a 4-literal chain, a MIXED chain (`"lit" + x + "a" + "b"` folds only the adjacent literal run, runtime concat keeps the variable), a global string initializer folded to a static literal, folded chains feeding `==`, and folded results passed through a function boundary. Byte-exact on all compilers — the fold must never change observable output, only where the bytes live.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
let g : string = "global left part, " + "global right part";

fun wrap(s: string) -> string {
    return "[" + s + "]";
}

fun mixed(x: string) -> string {
    return "prefix [" + x + "] suffix, " + "folded tail";
}

initial {
    print("a\n" + "b\tc");
    println("|");
    println("w" + "x" + "y" + "z");
    println(g);
    println(mixed("MID"));
    if ("a" + "b" == "ab") { println("eq ok"); }
    if ("a" + "b" != "ab") { println("eq bad"); }
    println(wrap("a" + "b" + "c"));
    println("quote:\"" + "slash:\\");
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
ExpectedStdout: EQUALS `a
b	c|
wxyz
global left part, global right part
prefix [MID] suffix, folded tail
eq ok
[abc]
quote:"slash:\
`
ExpectedStderr: DISCARD
