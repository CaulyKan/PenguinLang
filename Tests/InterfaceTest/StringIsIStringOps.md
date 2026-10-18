# StringIsIStringOps
## Description
`"..." is IStringOps` on a statically-known string folds to compile-time `true` — this locks in the fix for the old VM/native divergence (the BabyPenguin VM attached the impl's vtable to the string BasicTypeNode and said true, while native read `_emperor_string_metadata`'s empty interface_map and said false).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let s: string = "abc";
    println(cast<string>(s is IStringOps));
    println(cast<string>(s.length()));
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
3
`
ExpectedStderr: DISCARD
