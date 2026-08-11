# MetaCurrentScopeMultiple
## Description
Save/restore correctness of `current_scope_class_name`: two classes (`Alpha`, `Beta`) each splice `#who_am_i()`, and EACH splice site must see ITS OWN enclosing class. `who_am_i` reads `compiler().get_current_scope()` and injects a class method `scope_name(this)` returning the class name. `a.scope_name()` must print `Alpha` and `b.scope_name()` must print `Beta` — if the 5a rewrite failed to reset the scope between containers, both would report the same class. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Alpha { #who_am_i(); }
class Beta { #who_am_i(); }
#fun who_am_i() -> ast {
    let t: emperor.BoundType = compiler().get_current_scope();
    let n: string = t.display_name();
    return compiler().create_definition(
        "fun scope_name(this) -> string { return \"" + n + "\"; }");
}
initial {
    let a: mut Alpha = new Alpha();
    let b: mut Beta = new Beta();
    println(a.scope_name());
    println(b.scope_name());
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
ExpectedStdout: EQUALS `Alpha
Beta
`
ExpectedStderr: DISCARD
