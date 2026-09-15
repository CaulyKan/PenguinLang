# MetaUserSources
## Description
`--meta-with-user-sources`: the meta engine's unit B includes the compilation's own opted-in user source files (marked with a `// meta: unit-b` line and free of coroutine/topology constructs), so a `#fun` body can call ORDINARY USER FUNCTIONS. Previously unit B saw only the compiler's base sources + other #funs — a #fun calling a user function failed with "Cannot resolve symbol". The #fun is synthesized wrapped in its declaring namespace (with the file's `using`s) plus a bare-name top-level forwarder so the JIT lookup keeps working. The example-scale consumer is the tinyriscv model (compile-time assembler). Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
#fun baked_call() -> i64 {
    return trv_double(21);
}

#fun baked_string() -> string {
    return trv_tag("meta");
}

initial {
    println("n=" + cast<string>(#baked_call()));
    println("s=" + #baked_string());
}
```

## Compile
Args: `Tests/fixtures/meta_unit_b_lib.penguin --meta-with-user-sources`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `n=42
s=[meta]
`
ExpectedStderr: DISCARD
