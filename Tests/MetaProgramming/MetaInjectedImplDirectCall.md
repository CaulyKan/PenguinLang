# MetaInjectedImplDirectCall
## Description
Calling a `#specializing`-injected interface method DIRECTLY on a specialized enum-typed value from user code: `x.get_unique_name()` where `x = new Option<i64>.some(42)`. The `#specializing __builtin.Option<T>` block in core_builtin injects the `IUniqueMangleName` impl into the `Option<i64>` specialization (native `umangleable(T)` gate); this test pins that the injected method resolves through the specialized enum's scope at an ordinary call site — previously unverified (an earlier draft of this test used the bare `Option<i64>.some(42)` construction, which is now correctly rejected — see BareVariantConstructionError). Output is the injected impl's rendering: `"Op_s_" + payload name`.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let x: Option<i64> = new Option<i64>.some(42);
    println(x.get_unique_name());
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
ExpectedStdout: EQUALS `Op_s_42
`
ExpectedStderr: DISCARD
