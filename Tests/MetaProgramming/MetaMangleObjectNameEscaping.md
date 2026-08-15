# MetaMangleObjectNameEscaping
## Description
Object value-template args are mangled by the IUniqueMangleName canonical name, and that name must ENCODE free-text content injectively: `__builtin.umangle_escape` maps `_` → `_0`, other non-alphanumerics → `_xHH`, so StringBuilder("a b") → `SB_a_x20b` and StringBuilder("a_b") → `SB_a_0b` — distinct mangles. Before escaping, both names sanitized to `SB_a_b` at the mangle level, so the two `new W<#make_sb...>()` sites wrongly deduped to ONE specialization and the second site ran the first site's substituted body. The two builders are separate #funs (string literals inside #fun bodies are unit-B code; a string literal as a TYPE-position meta-call argument is not yet supported by eval_meta_type_call_args). The observable is the char code at index 1 of the content (32 for ' ' vs 95 for '_'), spliced via `#sb_code_at(B, 1)`. Pass3 only (object args are JIT-only).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun make_sb_sp() -> StringBuilder {
    let b = new StringBuilder();
    b.append("a");
    b.append(" ");
    b.append("b");
    return b;
}
#fun make_sb_us() -> StringBuilder {
    let b = new StringBuilder();
    b.append("a");
    b.append("_");
    b.append("b");
    return b;
}
#fun sb_code_at(b: StringBuilder, i: i64) -> i64 {
    return string_char_code(string_char_at(b.to_string(), i));
}
#template(B: StringBuilder) class W {
    c: i64 = #sb_code_at(B, 1);
}
initial {
    println(cast<string>(new W<#make_sb_sp()>().c));
    println(cast<string>(new W<#make_sb_us()>().c));
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
ExpectedStdout: EQUALS `32
95
`
ExpectedStderr: DISCARD
