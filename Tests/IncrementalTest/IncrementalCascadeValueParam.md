# IncrementalCascadeValueParam
## Description
O1 sentinel (per-file A4 collector): a VALUE-PARAMETER class template (`#template(N: i32) class Arr`, the D6 value-generic form) owned by a CLEAN file must instantiate through the dirty file's body edit — via BOTH paths: the resolve chain (Host<T>'s field `mut Arr<3>` instantiates Arr$3 while the Host<i32> specialization is being constructed — the resolve-cascade funnel) and the fixpoint cascade (Host's constructor body `new Arr<3>()` re-requested from the new specialization's template-body scan; both requests dedup against the same instance). Guards the gated sweeps' handling of value args (is_value_arg / value_arg_text in resolve_single_generic_arg and the new-expression collector) plus the construction-vs-scan interplay. NOTE: the qualified value-generic FUNCTION call form (`ns.f<3>()`) is a separate, pre-existing full-compile gap (filed as its own sentinel) and is deliberately not exercised here.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("plain");
}
```
## Compile
Args: `--disable-coroutine --incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/valueparam_v2.penguin Tests/IncrementalTest/valueparam_meta.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedStdout: EQUALS `report=37
`
ExpectedExitCode: 0
