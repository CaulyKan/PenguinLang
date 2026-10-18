# PrimitiveCastStandaloneError
## Description
The negative side of the interface-cast immediate-call exception: a primitive-to-interface cast standing ALONE (assignment to a slot, passing as an argument) is a compile error on both compilers — interface-typed storage of an unboxed primitive is not supported; use Box<T> for true polymorphism. Implicit `let b : I = p` (no cast) is likewise rejected by the assignment checks.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a: i64 = 42;
    let b: IHash = cast<IHash>(a);
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
