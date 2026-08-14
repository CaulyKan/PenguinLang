# MetaFunRefReturnSplice
## Description
NEGATIVE (4c guard): a #fun with a REFERENCE-typed return may NOT be spliced into runtime code — the caller-stub hands back the compile-time ADDRESS, which dangles in the generated program. `let s: StringBuilder = #make_sb();` in an initial block must fail with E_UNSUPPORTED ("cannot be spliced into runtime code; use it as a template argument or introspect it inside another #fun"). Reference returns are compile-time-only flows (template args / #-introspection). Compile exit NONZERO on Pass3 (and Pass2: the unit-B body itself also rejects... Pass2 has no JIT so #make_sb() is not a known #fun there — still a compile failure, though via a different error; both NONZERO). 

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun make_sb() -> StringBuilder {
    let mut sb = new StringBuilder();
    sb.append("nope");
    return sb;
}
initial {
    let s: StringBuilder = #make_sb();
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
