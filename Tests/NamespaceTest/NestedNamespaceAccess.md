# NestedNamespaceAccess
## Description
Nested namespace member access (depth-2+ qualification, e.g. `outer.inner.value()`): the parser and scope layer always supported nested `namespace` blocks, but bind_member_access only continued namespace-qualified lookup from an IDENTIFIER base — the inner `outer.inner` binds as a member access carrying the nested namespace symbol, so every deeper call fell to the void fallback (`E_INTERNAL: Function call has no callee symbol`). The fix chains the namespace lookup from that inner symbol. This is the enabling fix for the `std.io` stdlib layout. BabyPenguin's own compiler does not resolve nested namespace member access yet (EmperorPenguin-only behavior), hence no BabyPenguin/Pass1 in Apply To.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace outer {
    namespace inner {
        fun value() -> i64 { return 42; }
        fun deep() -> i64 {
            return outer.inner.value() - 40;
        }
    }
}
initial {
    println("v:" + cast<string>(outer.inner.value()));
    println("d:" + cast<string>(outer.inner.deep()));
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
ExpectedStdout: EQUALS `v:42
d:2
`
ExpectedStderr: DISCARD
