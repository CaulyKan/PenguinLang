# MetaJsonGenericStaticCall
## Description
RED SENTINEL (known bug, kept as a stable failure until fixed): a GENERIC type's injected `#specializing` impl method called STATICALLY in the json context — `Option<i64>.json_deserialize(...)` / `Box<i64>.json_deserialize(...)` where the impl is injected by json.penguin's own `#specializing __builtin.Option<T>` / `#specializing __builtin.Box<T>` blocks. The blocks ARE registered (pass 1) but the injection does not take effect when json.penguin is compiled as part of the full std file set (json+vector+hashmap): the specialized `Option__i64`/`Box__i64` defs carry no injected impl, so the static member lookup fails with "Type 'Option__i64' has no member 'json_deserialize'". The SAME syntax works when the `#specializing` block lives in the user's own file (see MetaGenericStaticCall / the two-file s10 experiment), so the bug is specific to json.penguin's blocks + the std file set — a regression sentinel for the generic-static-call feature's json adoption. Should turn green once json's Option/Box blocks inject reliably.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let probe = new __builtin.Option<i64>.none();
    let v = Option<i64>.json_deserialize("5");
    if (v is __builtin.Option<i64>.some) { println("v=" + cast<string>(v.some)); }
}
```

## Compile
Args: `EmperorPenguin/std/penguin/json.penguin EmperorPenguin/std/penguin/hashmap.penguin EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `v=5
`
ExpectedStderr: DISCARD
