# MutParamCopyDirect
## Description
Value-copy semantics: a plain `mut` parameter (`fun setX(p: mut Point, ...)`)
receives a COPY of the caller's value-class argument — `mut` on a parameter is
a permission to mutate the local copy, never storage identity (only method
receivers `mut this` alias the caller's slot). Writes inside the callee are
not visible to the caller — prints 1. Direct class form of the enum-payload
variant EnumVariantPassToFunctionTest.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point { x: i32 = 0; y: i32 = 0; fun new(mut this, x: i32, y: i32) { this.x = x; this.y = y; } }

fun setX(p: mut Point, v: i32) {
    p.x = v;
}

initial {
    let mut a = new Point(1, 2);
    setX(a, 9);
    print(cast<string>(a.x));
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
ExpectedStdout: EQUALS `1`
ExpectedStderr: DISCARD
