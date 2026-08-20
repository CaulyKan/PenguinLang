# EnumValueSize
## Description
Value-type enum sizes must follow the real `{ ptr metadata, i64 tag, [N x i8] payload }`
LLVM struct layout: the payload is a fixed 16-byte header (meta ptr + i64 tag)
followed by the largest payload size, total aligned to 8 — the byte-array
union keeps whole-enum copies byte-preserving for every variant (a typed union
let the optimizer split copies at one variant's field boundaries and truncate
same-offset pointers of another variant). So `Option<i32>` is 16+4 → 24, not
the pre-layout-fix constant 24 that sized every payload as a pointer — sizes
now track the REAL payload (Option<i32>=24, Option<i64>=24, a 24-byte value
class would be 40). Also checks tag-only enums (16), pointer payloads
(Option<string> = 24) and the largest-payload-wins union rule (`i64` beats
`i32` → 24). BabyPenguin's ANTLR grammar does not parse meta calls, so Apply To is
EmperorPenguin-only. Verified on Pass1/Pass2/Pass3.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point {
    x: i32;
    y: i32;
}
enum TagOnly { a; b; }
enum I32Payload { a; b: i32; }
enum MixedPayload { a; b: i32; c: i64; }
initial {
    println("tag=" + cast<string>(#sizeof(TagOnly)));
    println("i32p=" + cast<string>(#sizeof(I32Payload)));
    println("mixed=" + cast<string>(#sizeof(MixedPayload)));
    println("opt_i32=" + cast<string>(#sizeof(Option<i32>)));
    println("opt_i64=" + cast<string>(#sizeof(Option<i64>)));
    println("opt_str=" + cast<string>(#sizeof(Option<string>)));
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
ExpectedStdout: EQUALS `tag=16
i32p=24
mixed=24
opt_i32=24
opt_i64=24
opt_str=24
`
ExpectedStderr: DISCARD
