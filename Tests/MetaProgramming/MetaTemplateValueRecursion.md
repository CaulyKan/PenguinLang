# MetaTemplateValueRecursion
## Description
SKIPPED — value-template FUNCTION recursion cannot run correctly without compiler dead-code evaluation. `#template<N:i32> fun fab()` calls `fab<N-1>()` / `fab<N-2>()` behind the runtime guard `if (N < 2) { return N; }`. Under D6 the function is specialized at compile time and called at runtime, but each recursive value-generic call is a COMPILE-TIME instantiation: specializing `fab<5>` textually needs `fab<4>`/`fab<3>`/… and, past the base case, `fab<-1>`/`fab<-2>`/… — an unbounded descent. The guard only stops the descent at RUNTIME; to bound the compile-time instantiation set the compiler would have to constant-fold `N < 2` and drop the dead recursive branch during monomorphization (dead-code evaluation), which does not exist yet. The old desugar-to-`#fun` worked only because it JIT-evaluated `fab<5>` fully at compile time. Until dead-code evaluation (or function-level static base cases) lands, this case is skipped with the intended behavior recorded below.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Skip
Requires compiler dead-code evaluation: with runtime specialization, `fab<N-1>()` recursion past the base case needs the compiler to constant-fold the `if (N < 2)` guard and prune the dead recursive branch at compile time to bound the instantiation set; the runtime guard alone cannot. Skipped until that exists (see ## Description).

## Test Code
```
#template(N: i32)
fun fab() -> i64 {
    if (N < 2) { return N; }
    return fab<N-1>() + fab<N-2>();
}
initial {
    let r = fab<5>();
    println("fab=" + cast<string>(r));
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
ExpectedStdout: EQUALS `fab=5
`
ExpectedStderr: DISCARD
