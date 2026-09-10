# StringRelationalGeNative
## Description
Regression test (was a RED SENTINEL, fixed 2026-09-03): relational operators (`<`, `>`, `<=`, `>=`) on strings — plus char LITERALS everywhere — were broken on EVERY backend, each in its own way. Native (EmperorPenguin) lowered string relationals to a raw pointer `icmp` on the two `_emperor_string*` pointers, comparing literal/heap ADDRESSES (content-independent garbage: `"t" >= "a"` was observed false, `"t" <= "z"` true only by allocation-order luck — eq/ne already routed through `_emperor_string_equal`). The BabyPenguin VM's `BinCmpLt/Gt/Le/Ge` had no `TypeEnum.String` (nor `Char`) arm and silently returned `false` for every relational (so the original "green on BabyPenguin" assumption never held). The C# backend emitted `string < string`, which does not compile in C#. Char literals: the VM's `MakeValue` took `literal[0]` of the raw token text — the opening QUOTE (39), so every char constant was 39 (`cast<i64>('a')` printed 39) — and native emitted the quoted text verbatim (`add i32 0, 'a'`, invalid LLVM; `'e'` was even misread as a float by the literal-shape test). Fix: new strcmp-style `_emperor_string_compare` runtime helper + LLVM-emitter routing (like eq/ne), `string.CompareOrdinal` arms in the VM and C# backend, `Char` arms in the VM, char-literal unquote+unescape in the VM's `MakeValue` and code-point binding in EP's `bind_constant`. The libmeta source-file scanner's integer-based `string_char_code_at` variant stays as-is (cheaper, still correct).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass3

## Test Code
```
initial {
    let t: string = "t";
    println("ge=" + cast<string>(t >= "a"));
    println("le=" + cast<string>(t <= "z"));
    println("geq_same=" + cast<string>(t >= "t"));
    println("gt=" + cast<string>(t > "a"));
    println("range=" + cast<string>(t >= "a" && t <= "z"));
    let a: char = 'a';
    let z: char = 'z';
    println("char_range=" + cast<string>(a < z));
    println("char_ge_same=" + cast<string>(a >= a));
    println("char_code=" + cast<string>(cast<i64>(a)));
}
```
## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `ge=true
le=true
geq_same=true
gt=true
range=true
char_range=true
char_ge_same=true
char_code=97
`
ExpectedStderr: DISCARD
