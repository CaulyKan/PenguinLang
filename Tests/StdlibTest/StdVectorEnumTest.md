# StdVectorEnumTest
## Description
Regression for the Vector<Enum> segfault: `BoundType.byte_size()` for an enum infinite-recursed on tag-only variants (their `member_type` is set to the enum's own type as a tag-only sentinel by resolve_enum_types, but byte_size only skipped void payloads). `Vector<JV2>` (an enum element) used to crash the compiler with a segfault (exit 139); fixed by skipping the enum's own type like void (mirroring build_enum_layout's is_self_enum). This test constructs `penguin.Vector<JV2>`, pushes an enum value, and reads it back — also exercises `#sizeof(enum)` in the Vector constructor. Pass3-only (pointer IR; EmperorPenguin-native).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
namespace penguin {
enum JV2 {
    a;
    b: i64;
}
}
initial {
    let mut v = new penguin.Vector<penguin.JV2>();
    v.push(new penguin.JV2.b(5));
    v.push(new penguin.JV2.b(6));
    println("n0=" + cast<string>(v.at(0).some.b));
    println("n1=" + cast<string>(v.at(1).some.b));
    let sum: mut i64 = 0;
    for (let e in v) { sum = sum + e.b; }
    println("sum=" + cast<string>(sum));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `n0=5
n1=6
sum=11
`
ExpectedStderr: DISCARD
