# MetaTemplateMixedGenerics
## Description
req2+req5: type-generic and value-generic instantiation used together. `new Box<i64>(42)` is a normal type-generic instantiation; `new Arr<3>()` is a value-template class (req5, N substituted into the field default). Both `<...>` forms must parse and resolve correctly in the same scope. Exercises both generic-arg kinds side by side.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class Box {
    v: mut T;
    fun new(mut this, x: T) { this.v = x; }
}
#template(N: i32)
class Arr {
    size: mut i64 = N;
}
initial {
    let b = new Box<i64>(42);
    let a = new Arr<3>();
    println("v=" + cast<string>(b.v) + " size=" + cast<string>(a.size));
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
ExpectedStdout: EQUALS `v=42 size=3
`
ExpectedStderr: DISCARD
