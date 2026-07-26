# MetaFunRecursion
## Description
Phase 5b: a recursive `#fun` (fibonacci). Inside the `#fun` body the self-call is an ordinary recursive call (`fib(n - 1)` / `fib(n - 2)`, no `#`); the user-code call `#fib(10)` JIT-executes the whole recursion at compile time and splices the result (55). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun fib(n: i64) -> i64 {
    if (n < 2) { return n; }
    return fib(n - 1) + fib(n - 2);
}
initial {
    let x: i64 = #fib(10);
    println("fib=" + cast<string>(x));
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
ExpectedStdout: EQUALS `fib=55
`
ExpectedStderr: DISCARD
