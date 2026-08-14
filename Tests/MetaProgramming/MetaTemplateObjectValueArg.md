# MetaTemplateObjectValueArg
## Description
An OBJECT (StringBuilder — the stdlib type that impls IUniqueName) as a non-type template argument with PROPER reference semantics — the compiler converts at the ABI boundary, no manual unsafe_cast: `#fun make_sb() -> StringBuilder` RETURNS the reference directly (the caller-stub ptrtoint's the ptr to the i64 address for the trampoline), and `#fun sb_len(b: StringBuilder)` TAKES the reference param directly (the stub materializes the live pointer via penguin_meta_get_object). `#template(B: StringBuilder) class Foo`'s field initializer `#sb_len(B)` substitutes B -> the address and introspects the SAME live object at compile time — foo=3 (len of 'abc'). #fun bodies run in unit B (base_meta_sources only: stdlib types unqualified, never user classes). Runtime-constant splicing of reference returns is rejected by design (dangling compile-time address). The arg is an OBJECT value arg mangled by its IUniqueName canonical name (SB_abc) — stable across compilations (see MetaTemplateObjectDedup for the two-site dedup case). Pass3-only (pass2 is a stub-config build without the meta JIT).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun make_sb() -> StringBuilder {
    let mut sb = new StringBuilder();
    sb.append("abc");
    return sb;
}
#fun sb_len(b: StringBuilder) -> i64 {
    return string_length(b.to_string());
}
#template(B: StringBuilder)
class Foo {
    foo: i64 = #sb_len(B);
}
initial {
    let a = new Foo<#make_sb()>();
    println("foo=" + cast<string>(a.foo));
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
ExpectedStdout: EQUALS `foo=3
`
ExpectedStderr: DISCARD
