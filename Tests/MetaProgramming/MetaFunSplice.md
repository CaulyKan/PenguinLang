# MetaFunSplice
## Description
Phase 5b: `#fun` meta functions called in user code are JIT-executed at compile time and spliced as constants. Covers arity 1 (`#sq`, i64), arity 2 (`#add`, i32 params + return), a `u32`-returning function (`#dbl`), and two splices composed in one expression (`#sq(6) + #sq(2)`). Requires native Pass2/Pass3 (the JIT only links there).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun sq(n: i64) -> i64 { return n * n; }
#fun add(a: i32, b: i32) -> i32 { return a + b; }
#fun dbl(n: u32) -> u32 { return n * 2; }
initial {
    let a: i64 = #sq(5);
    let b: i32 = #add(3, 4);
    let c: u32 = #dbl(10);
    let d: i64 = #sq(6) + #sq(2);
    println("a=" + cast<string>(a));
    println("b=" + cast<string>(b));
    println("c=" + cast<string>(c));
    println("d=" + cast<string>(d));
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
ExpectedStdout: EQUALS `a=25
b=7
c=20
d=40
`
ExpectedStderr: DISCARD
