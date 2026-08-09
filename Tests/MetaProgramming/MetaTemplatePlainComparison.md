# MetaTemplatePlainComparison
## Description
Regression guard for the value-generic `<`/`>` parser backtracking: ordinary comparisons (`<`, `>`, chained with `&&`) must still parse correctly after the value-generic-call path was added. Pure comparison program (no value template), so no meta JIT is needed — it runs on all EmperorPenguin passes (where the parser change lives).

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let m: i64 = 3;
    let n: i64 = 5;
    if (m < n) { println("less"); }
    if (n > m) { println("greater"); }
    if (m < n && n > m) { println("both"); }
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
ExpectedStdout: EQUALS `less
greater
both
`
ExpectedStderr: DISCARD
