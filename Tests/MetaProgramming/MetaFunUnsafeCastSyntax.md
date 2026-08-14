# MetaFunUnsafeCastSyntax
## Description
NEGATIVE (parser): unsafe_cast REQUIRES the <T> type argument — `unsafe_cast(c)` without it is a parse error (Expected '<'), failing compilation. Guards the keyword's grammar against silent misparse (the Lexer token must not swallow following tokens). Compile exit NONZERO on Pass2/Pass3 (both use the self-hosted parser).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class C {
    x: i64 = 0;
}
initial {
    let c = new C();
    let p: i64 = unsafe_cast(c);
    println("unreachable");
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
