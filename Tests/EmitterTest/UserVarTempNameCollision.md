# UserVarTempNameCollision
## Description
User variables named like the compiler's internal temporaries (`t2`, `t3`, `t5`, ...) must not collide with register names. Found during iterator-combinator work as a real link failure (`multiple definition of local value named 't2'`, reproduced in two shapes: the assign-normalization redefining its own source `%t2 = add i64 0, %t2`, and a temp register `%t5 = add i64 %t5, %t4` colliding with a user-named register). Fixed by two guards: emit_assign skips the normalize-add when destination equals source, and IRFunction.make_unique_reg_name unconditionally reshapes `t<digits>`-shaped user register names (`t5` -> `t5_v`) since IRTempRegister displays as %tN. This locks the fix in; BabyPenguin (the reference) prints `x13y42z!8` either way.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun f(a: i64, b: i64) -> string {
    let t2: i64 = a + b;
    let t3: i64 = a * b;
    let s: string = "x" + cast<string>(t2) + "y" + cast<string>(t3) + "z";
    return s;
}
initial {
    let t5: i64 = 7;
    println(f(t5, 6) + "!" + cast<string>(t5 + 1));
}
```

## Compile
Args: ``
Env: ``
ExpectedStdout: DISCARD
ExpectedExitCode: 0
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `x13y42z!8
`
ExpectedStderr: DISCARD
