# MetaCurrentScopeEmpty
## Description
`compiler().get_current_scope()` when NOT inside a class returns an empty `BoundType` (display_name() == "void"). `#scope_probe()` is called from `initial` (not a class member), so the responder's `current_scope_class_name` is empty (the 5a rewrite save/restores it). Asserts `t.display_name() == "void"`. Also guards the save/restore: if any class rewrite left the scope name set, this test turns red. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun scope_probe() -> i64 {
    let t: emperor.BoundType = compiler().get_current_scope();
    if (t.display_name() == "void") { return 1; }
    return 0;
}
initial {
    println("sc=" + cast<string>(#scope_probe()));
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
ExpectedStdout: EQUALS `sc=1
`
ExpectedStderr: DISCARD
