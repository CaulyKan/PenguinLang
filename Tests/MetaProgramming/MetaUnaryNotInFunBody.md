# MetaUnaryNotInFunBody
## Description
Unary operators in a `#fun` body. UnaryExpression.build_text compared `cast<string>(operator_value)` — cast<string> of an enum yields its numeric INDEX, so the name checks never matched and every unary op rendered as its index (`-v` -> `0v`), breaking unit-B synthesis of any #fun using unary ops. Fixed by comparing with `is` on the enum value. `#neg_i(5)` asserts `-` now synthesizes and JIT-evaluates correctly. (`!` is exercised separately in MetaBoolArgBug — bool ARGS in the meta trampoline currently evaluate wrong, so a `!`-based bool test would be confounded.) Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun neg_i(v: i64) -> i64 { return -v; }
initial {
    println("neg=" + cast<string>(#neg_i(5)));
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
ExpectedStdout: EQUALS `neg=-5
`
ExpectedStderr: DISCARD
