# QualifiedInterfaceIs
## Description
`x is <namespace>.<Interface>` with a QUALIFIED interface name folds/judges the same as the short name. Known gap: EmperorPenguin currently mis-lowers a qualified interface name on the `is` RHS (the member-access-shaped type reference falls into the false-constant fallback) — Apply To stays BabyPenguin-only until that is fixed; extend to the EP compilers once `x is __builtin.IHash` agrees.

## Apply To
* BabyPenguin

## Test Code
```
initial {
    let a: i64 = 1;
    println(cast<string>(a is __builtin.IHash));
    let s: string = "x";
    println(cast<string>(s is __builtin.IStringOps));
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
ExpectedStdout: EQUALS `true
true
`
ExpectedStderr: DISCARD
