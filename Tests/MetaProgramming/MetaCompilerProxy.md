# MetaCompilerProxy
## Description
The `#compiler()` facade returns a REAL proxy object: `let c = compiler()` stores it, and member calls (`c.has_type(...)`) bind as method calls on the unit-B `CompilerContext` class (each forwarding to an `emperor.penguin_meta_*` host responder). Locks the real-proxy-object path (`let c = compiler()` + stored-object member calls), distinct from the inline `compiler().member()` form used elsewhere. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun probe() -> i64 {
    let c = compiler();
    if (c.has_type("i32")) { return 1; }
    return 0;
}
initial {
    println("proxy=" + cast<string>(#probe()));
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
ExpectedStdout: EQUALS `proxy=1
`
ExpectedStderr: DISCARD
