# ArrayUserClassShadowWrapper
## Description
A user-defined top-level `class Box` must not be confused with the builtin generic wrapper `__builtin.Box<T>` by the `umangleable()` gate on the `#specializing __builtin.Option<T>` block (which injects `impl IUniqueMangleName` calling `.get_unique_name()` on the payload). The gate used to match the wrapper by SHORT name, so a user `Box` with zero generic args was reported umangleable, the impl was injected on `Option<Box>`, and binding its body failed with `Type 'Box' has no member 'get_unique_name'` (EmperorPenguin; fixed by matching full names `_utils.List`/`__builtin.Box`/`__builtin.Option`/`__builtin.Result`/`__builtin.Pair`). The for-in loop is what triggers the `Option<Box>` specialization and the gate.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Box {
    impl IReferenceType;
    v: i64;
    fun new(mut this, v: i64) {
        this.v = v;
    }
}
initial {
    let a : mut array<Box> = create_array(3, new Box(-1));
    let i : mut u64 = 0;
    while (i < 3) {
        a.set(i, new Box(cast<i64>(i) + 1));
        i = i + 1;
    }
    let total : mut i64 = 0;
    for (let b in a) {
        total += b.v;
    }
    println("sum=" + cast<string>(total));
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
ExpectedStdout: EQUALS `sum=6
`
ExpectedStderr: DISCARD
