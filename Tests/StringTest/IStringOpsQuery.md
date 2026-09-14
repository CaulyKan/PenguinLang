# IStringOpsQuery
## Description
End-to-end test of `__builtin.IStringOps` — the std string-operation interface implemented for the primitive `string` (`impl IStringOps for string`, mirrored in BabyPenguin/Builtin.penguin and EmperorPenguin/std/penguin/core_builtin.penguin). This case covers the query/index/predicate/conversion half: length, is_empty, char_at, char_code, char_code_at, substring, slice, find, find_from, find_last, contains, starts_with, ends_with, to_int, to_double — including out-of-range and empty-string edge behavior (char_at OOB → "", find_last of "" → -1 per the documented empty-needle rule). Method calls dispatch through the primitive interface path on every compiler (vtable on the string BasicTypeNode for BabyPenguin, direct `$$string` calls on EmperorPenguin). Verified on all five compilers (BabyPenguin VM + CS, EmperorPenguin Pass1/Pass2/Pass3).

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let s: string = "Hello, Penguin!";
    println("len=" + cast<string>(s.length()));
    println("empty=" + cast<string>("".is_empty()));
    println("nonempty=" + cast<string>(s.is_empty()));
    println("char_at=" + s.char_at(7));
    println("char_at_oob=" + s.char_at(99));
    println("char_code=" + cast<string>(s.char_code()));
    println("char_code_empty=" + cast<string>("".char_code()));
    println("char_code_at=" + cast<string>(s.char_code_at(1)));
    println("char_code_at_oob=" + cast<string>(s.char_code_at(-1)));
    println("substring=" + s.substring(7, 7));
    println("substring_clamped=" + s.substring(10, 100));
    println("slice=" + s.slice(7, 7));
    println("find=" + cast<string>(s.find("Penguin")));
    println("find_miss=" + cast<string>(s.find("fish")));
    println("find_empty=" + cast<string>(s.find("")));
    println("find_from=" + cast<string>("a-b-c".find_from("-", 2)));
    println("find_from_miss=" + cast<string>("a-b-c".find_from("x", 0)));
    println("find_last=" + cast<string>("a-b-c-b".find_last("b")));
    println("find_last_miss=" + cast<string>("a-b-c".find_last("b2")));
    println("find_last_empty=" + cast<string>("abc".find_last("")));
    println("contains_t=" + cast<string>(s.contains("Penguin")));
    println("contains_f=" + cast<string>(s.contains("penguin")));
    println("starts_with_t=" + cast<string>(s.starts_with("Hello")));
    println("starts_with_f=" + cast<string>(s.starts_with("hello")));
    println("starts_with_empty=" + cast<string>(s.starts_with("")));
    println("ends_with_t=" + cast<string>(s.ends_with("!")));
    println("ends_with_prefix=" + cast<string>(s.ends_with("Penguin!")));
    println("ends_with_f=" + cast<string>(s.ends_with("?")));
    println("ends_with_longer=" + cast<string>("ab".ends_with("abc")));
    println("to_int=" + cast<string>("42".to_int()));
    println("to_int_neg=" + cast<string>("-7".to_int()));
    println("to_double=" + cast<string>("2.5".to_double()));
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
ExpectedStdout: EQUALS `len=15
empty=true
nonempty=false
char_at=P
char_at_oob=
char_code=72
char_code_empty=-1
char_code_at=101
char_code_at_oob=-1
substring=Penguin
substring_clamped=guin!
slice=Penguin
find=7
find_miss=-1
find_empty=0
find_from=3
find_from_miss=-1
find_last=6
find_last_miss=-1
find_last_empty=-1
contains_t=true
contains_f=false
starts_with_t=true
starts_with_f=false
starts_with_empty=true
ends_with_t=true
ends_with_prefix=true
ends_with_f=false
ends_with_longer=false
to_int=42
to_int_neg=-7
to_double=2.5
`
ExpectedStderr: DISCARD
