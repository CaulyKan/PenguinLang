# MetaTemplateValueExprArith
## Description
req3 ambiguity: value-template call results combined with arithmetic. `dbl<5>() + dbl<3>()` and `dbl<10>() - dbl<5>()` — the parser must close each value-generic arg list at `>`, apply the `()` call, then treat the following `+`/`-` as ordinary arithmetic (not part of a generic arg list). Guards against the value-generic `<`/`>` backtracking over-consuming operators. Requires native Pass2/Pass3 (meta JIT).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
fun dbl() -> i64 { return N * 2; }
initial {
    let x = dbl<5>() + dbl<3>();
    let y = dbl<10>() - dbl<5>();
    println("x=" + cast<string>(x) + " y=" + cast<string>(y));
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
ExpectedStdout: EQUALS `x=16 y=10
`
ExpectedStderr: DISCARD
