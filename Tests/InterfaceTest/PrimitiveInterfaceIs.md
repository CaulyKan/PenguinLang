# PrimitiveInterfaceIs
## Description
Static `is I` folding on primitives (primitive interfaces round): every primitive implementing an interface via `impl I for <prim>` (IHash over i64/f64/bool/char/string) folds to a compile-time `true`; an interface with no impl for that primitive folds to `false`. Direct method calls (`a.hash()`, `s.hash()`) keep working. Verified on BabyPenguin and EmperorPenguin Pass1; the folding makes native agree with the VM (native used to read string metadata's empty interface_map and return false).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
interface INothing {}

initial {
    let a: i64 = 1;
    let f: f64 = 2.0;
    let b: bool = true;
    let c: char = 'A';
    let s: string = "x";
    println(cast<string>(a is IHash));
    println(cast<string>(f is IHash));
    println(cast<string>(b is IHash));
    println(cast<string>(c is IHash));
    println(cast<string>(s is IHash));
    println(cast<string>(a is INothing));
    println(cast<string>(s is INothing));
    println(cast<string>(a.hash()));
    println(cast<string>(s.hash()));
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
true
true
true
false
false
1
3414842651491571463
`
ExpectedStderr: DISCARD
