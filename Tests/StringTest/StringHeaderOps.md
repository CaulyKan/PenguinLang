# StringHeaderOps
## Description
Locks in the header-prefixed string representation (`metaptr + length + data + '\0'`, emperor_string.h / docs/impl-notes/23 §2.4) across the whole builtin surface: a 75KB string built through StringBuilder growth (header length must stay synced on the raw `data` field), then length/find/find_from/substring/char_code_at/starts_with_at/char_at/char_code/concat/==/to_int — every value below depends on the +16 data offset and the O(1) header length being correct. Byte-exact on all compilers.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun str_repeat(s: string, n: i64) -> string {
    let sb: mut StringBuilder = new StringBuilder();
    let i: mut i64 = 0;
    while (i < n) {
        sb.append(s);
        i = i + 1;
    }
    return sb.to_string();
}

initial {
    let big: string = str_repeat("ab-", 25000); // 75000 bytes
    println(cast<string>(string_length(big)));
    println(cast<string>(string_find(big, "ab-")));
    println(cast<string>(string_find_from(big, "-", 3)));
    println(string_substring(big, 74997, 4));
    println(cast<string>(string_char_code_at(big, 74999)));
    let joined: string = big + "!";
    println(cast<string>(string_length(joined)));
    if (joined == big) { println("eq-bad"); } else { println("eq-ok"); }
    println(cast<string>(string_starts_with_at(big, 74998, "b-")));
    println(cast<string>(string_char_code(string_char_at(big, 0))));
    println(cast<string>(string_to_int("-42")));
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
ExpectedStdout: EQUALS `75000
0
5
ab-
45
75001
eq-ok
true
97
-42
`
ExpectedStderr: DISCARD
