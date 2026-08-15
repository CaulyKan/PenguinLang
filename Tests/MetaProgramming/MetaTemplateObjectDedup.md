# MetaTemplateObjectDedup
## Description
R6b: value-level DEDUP + stable mangle of object value-template args via IUniqueMangleName. Two separate `#make_sb()` evaluations allocate two DIFFERENT live objects (different addresses) with the SAME logical value ("dedup") — both `new Foo<#make_sb()>()` sites must resolve to the SAME specialization: the arg is wrapped as an OBJECT value arg (make_object_value_arg) whose canonical unique_name ("SB_dedup", content-derived and IDENTIFIER-SAFE) is the mangle key, so the specialization is Foo__SB_dedup (stable across compilations — NOT the unstable raw address) and is_instantiation_already_exists dedups the second site. The template body introspects the live object at compile time (#sb_len(B) -> 5); both instances print 5. StringBuilder impls IUniqueMangleName in core_builtin; the host gates object args on the static type implementing IUniqueMangleName. Pass3-only (JIT).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun make_sb() -> StringBuilder {
    let mut sb = new StringBuilder();
    sb.append("dedup");
    return sb;
}
#fun sb_len(b: StringBuilder) -> i64 {
    return string_length(b.to_string());
}
#template(B: StringBuilder)
class Foo {
    n: i64 = #sb_len(B);
}
initial {
    let a = new Foo<#make_sb()>();
    let b = new Foo<#make_sb()>();
    println("n=" + cast<string>(a.n) + "," + cast<string>(b.n));
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
ExpectedStdout: EQUALS `n=5,5
`
ExpectedStderr: DISCARD
