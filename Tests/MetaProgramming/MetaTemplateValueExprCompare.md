# MetaTemplateValueExprCompare
## Description
req3 ambiguity (hardest case): comparing two value-template call results with `<` and `>`. `dbl<5>() < dbl<6>()` and `dbl<6>() > dbl<5>()` — after parsing the first value-generic call `dbl<5>()`, the parser must treat the next `<` as a comparison operator (not the start of another generic arg list), then parse the second `dbl<6>()` as a fresh value-generic call. This is the case most likely to be misparsed. Value-template calls are specialized at runtime (D6); no meta JIT needed.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(N: i32)
fun dbl() -> i64 { return N * 2; }
initial {
    if (dbl<5>() < dbl<6>()) {
        println("less");
    }
    if (dbl<6>() > dbl<5>()) {
        println("greater");
    }
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
`
ExpectedStderr: DISCARD
