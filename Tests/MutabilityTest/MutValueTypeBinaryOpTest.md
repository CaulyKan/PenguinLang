# MutValueTypeBinaryOpTest
## Description
Binary logical operators (&&, ||) on value types should work regardless of the mutability of the access path.
When a `mut` object's bool field is used with `&&` or `||` alongside a plain bool expression,
the mutability difference on the value type must not cause a type mismatch.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Data {
    flag: bool = true;
    fun new(mut this) {}
    fun check(this) -> bool {
        return this.flag;
    }
}

initial {
    let d: mut Data = new Data();
    if (d.flag && d.check()) {
        print("ok");
    }
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
ExpectedStdout: EQUALS `ok`
ExpectedStderr: DISCARD
