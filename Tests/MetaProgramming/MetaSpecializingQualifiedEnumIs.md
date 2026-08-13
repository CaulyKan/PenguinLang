# MetaSpecializingQualifiedEnumIs
## Description
A qualified generic enum-variant `is` check inside a `#specializing`-injected impl body lowers correctly to an ISENUM tag comparison. `this is __builtin.Option<T>.some` in the injected `has_value` returns true for a `some` value and false for `none`. This covers the namespace-qualified + generic-args enum variant form on the `is` RHS (`Namespace.Enum<Args>.variant`); the parser now attaches the generic args to the type member access even when followed by `.variant` (previously the `<Args>` was rejected and mis-parsed as a `<` comparison, folding the `is` to constant false).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace penguin {
#template(T: type)
interface IHasValue {
    fun has_value(this) -> bool;
}

#specializing __builtin.Option<T> {
    if (T.is_primitive()) {
        impl IHasValue {
            fun has_value(this) -> bool {
                return this is __builtin.Option<T>.some;
            }
        }
    }
}
}
initial {
    let o: __builtin.Option<i64> = new __builtin.Option<i64>.some(42);
    println("some=" + cast<string>(o.has_value()));
    let n: __builtin.Option<i64> = new __builtin.Option<i64>.none();
    println("none=" + cast<string>(n.has_value()));
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
ExpectedStdout: EQUALS `some=true
none=false
`
ExpectedStderr: DISCARD
