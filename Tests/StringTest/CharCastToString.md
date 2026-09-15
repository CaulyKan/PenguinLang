# CharCastToString
## Description
`cast<string>(char)` yields the CHARACTER itself (reference semantics: BabyPenguin's C# `char.ToString()`). Native (EmperorPenguin) had NO arm for char in the cast-to-string lowering — "char" is not one of the is_int_type spellings, so the cast fell through every case and emitted garbage code that segfaulted at runtime. Fixed by a dedicated arm calling the new `_emperor_char_to_string(i32)` runtime helper (UTF-8 encoded; ASCII = 1 byte). The C# backend was already correct (`op.ToString()`).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a: string = cast<string>(cast<char>(65));
    let b: string = cast<string>(cast<char>(66));
    let c: char = cast<char>(99);
    let s3: string = cast<string>(c);
    let nl: string = cast<string>(cast<char>(10));
    print(a + b + s3);
    print(nl);
    println("len_a=" + cast<string>(string_length(a)));
    println("code_back=" + cast<string>(string_char_code(s3)));
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
ExpectedStdout: EQUALS `ABc
len_a=1
code_back=99
`
ExpectedStderr: DISCARD
