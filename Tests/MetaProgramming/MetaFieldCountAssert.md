# MetaFieldCountAssert
## Description
R4 composite: a `#fun` uses reflection (`t.fields().size()`) + conditional `#error` to implement a compile-time assertion — `#assert_fewer_than_3(#typeof(Point))` passes (Point has 2 fields < 3); `#assert_fewer_than_3(#typeof(Big))` would fail (Big has 3 fields ≥ 3). Exercises reflection + diagnostics in combination. This test asserts the PASS case (compile succeeds). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: mut i32; y: mut i32; }
#fun assert_fewer_than_3(t: type) -> i64 {
    let n = cast<i64>(t.fields().size());
    if (n >= 3) {
        #error("type has too many fields");
    }
    return n;
}
initial {
    let count = #assert_fewer_than_3(#typeof(Point));
    println("fields=" + cast<string>(count));
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
ExpectedStdout: EQUALS `fields=2
`
ExpectedStderr: DISCARD
