# MetaCompilerResolve
## Description
By-name resolution via the `#compiler()` facade, all returning Option:
- `resolve_type("i32")` → `Option<BoundType>.some` with `display_name() == "i32"`
- `resolve_type("NoSuchTypeXYZ")` → `Option.none()` (unknown name)
- `resolve_symbol("GLOBAL_X")` → `Option<BoundSymbol>.some`, pattern-matches `BoundSymbol.variable`, and its `variable.bound_type.display_name() == "i64"`

`GLOBAL_X` is a top-level global variable (resolved from `global_scope` via `resolve_qualified`). Returns 1 only if all three hold. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
let GLOBAL_X: i64 = 0;
#fun resolve_probe() -> i64 {
    let t_opt = compiler().resolve_type("i32");
    if (t_opt.is_some()) {
        if (t_opt.some.display_name() == "i32") {
            let unknown = compiler().resolve_type("NoSuchTypeXYZ");
            if (unknown.is_none()) {
                let s_opt = compiler().resolve_symbol("GLOBAL_X");
                if (s_opt.is_some()) {
                    let s: emperor.BoundSymbol = s_opt.some;
                    if (s is emperor.BoundSymbol.variable) {
                        if (s.variable.bound_type.display_name() == "i64") { return 1; }
                    }
                }
            }
        }
    }
    return 0;
}
initial {
    println("r=" + cast<string>(#resolve_probe()));
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
ExpectedStdout: EQUALS `r=1
`
ExpectedStderr: DISCARD
