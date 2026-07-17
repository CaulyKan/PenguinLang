# NestedIfExprInCodeBlockTest
## Description
An if-expression inside a code block (which is the main branch of another if-expression)
should be correctly recognized as a trailing expression returning a value.
The grammar may parse the inner if as an if-statement; the compiler must handle this.

## Apply To
* BabyPenguin

## Test Code
```
fun get_val(flag: bool) -> i64 {
    if (flag) {
        return cast<i64>(42);
    }
    return cast<i64>(0);
}

initial {
    let i: i64 = 0;
    let bound_defs_size: i64 = 1;
    let result: i64 = if (i < bound_defs_size) {
        let val: i64 = get_val(true);
        if (val > cast<i64>(0)) {
            val
        } else {
            cast<i64>(0)
        }
    } else {
        cast<i64>(0)
    };
    print(result);
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
ExpectedStdout: EQUALS `42`
ExpectedStderr: DISCARD
