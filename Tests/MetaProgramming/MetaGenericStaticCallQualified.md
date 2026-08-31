# MetaGenericStaticCallQualified
## Description
Qualified-spelling generic static call: the generic type is spelled WITH its namespace (`__builtin.Box<i64>.tag()`) as the base of a member access, instead of the bare name (`Box<i64>.tag()`, see MetaGenericStaticCall). This exercises the namespace branch of member-access binding, which — before the fix this test locks in — dropped the member's generic args and resolved the bare TEMPLATE type, so the subsequent member lookup missed the `#specializing`-injected impl (which lives on the SPECIALIZED def `Box__i64` only) and failed with `error[E_RESOLVE_SYMBOL]: Type 'Box' has no member 'tag'`. This is exactly the spelling json.penguin's auto-impl generates for Option/Box fields (`json_type_spelling` emits the qualified source spelling), so it guards the `#impl_json_serializable()` Option/Box field path (see StdlibTest/MetaJsonOptionBoxFieldAutoImpl). Requires native Pass2/Pass3 (meta JIT for the specializing gate).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace st {
    interface IStat {
        fun tag() -> string;
    }
    #specializing __builtin.Box<T> {
        if (T.is_primitive()) {
            impl IStat {
                fun tag() -> string { return "W_stat_q"; }
            }
        }
    }
}
initial {
    let probe = new __builtin.Box<i64>(0);
    println(__builtin.Box<i64>.tag());
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
ExpectedStdout: EQUALS `W_stat_q
`
ExpectedStderr: DISCARD
