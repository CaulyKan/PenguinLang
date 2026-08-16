# ArraySetCallInFunction
## Description
Regression test (previously a RED SENTINEL): calling a method on a locally-constructed `std.Array<T,N>` inside a regular top-level function, and constructing `std.Array<T,N>` inside a loop in such a function. Before the fix, monomorphization's generic-instantiation collector scanned initial routines and class-member function bodies but NOT top-level function bodies — `new std.Array<i64,4>()` inside a plain function never registered its specialization, the specialized def never entered the bound tree, and `emit_new` found no class layout: the allocation was silently skipped and the following method call referenced an undefined this-register (`error: use of undefined value '%t0'` at clang). The same code in an `initial` block worked, which is why the pre-existing StdArrayTest never caught it. Fixed in SemanticModel.collect_generic_instantiations_from_ast_impl (top-level function_def branch now collects its body, same as class members). Green on Pass1/2/3.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun caseB() {
    let mut a = new std.Array<i64, 4>();
    a.set(0, 42);
    a.set(1, 7);
    println("v0=" + cast<string>(a.at(0).some));
    println("v1=" + cast<string>(a.at(1).some));
    let i: mut i64 = 0;
    while (i < 3) {
        let mut b = new std.Array<i64, 4>();
        b.set(0, cast<i64>(i));
        println("loop" + cast<string>(b.at(0).some));
        i = i + 1;
    }
}

initial {
    caseB();
    println("ok");
}
```

## Compile
Args: `EmperorPenguin/std/penguin/array.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `v0=42
v1=7
loop0
loop1
loop2
ok
`
ExpectedStderr: DISCARD
