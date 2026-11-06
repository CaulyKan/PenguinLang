# NestedNamespaceClassNew
## Description
Untyped class fields (`value = 7`, no `: type` annotation) are silently dropped by EmperorPenguin: resolve_class_pair only assigned a field's bound_type when the AST type_spec was present, so an untyped field kept the default void type, vanished from the class layout ([0 x i32] offsets) and member reads lowered to a literal `void` (clang "expected value token"); on `#class` shapes inside namespaces the downstream damage surfaced as E_INTERNAL "new expression has no type symbol" (handover bug #6, found during batch 5 MetaHost work). The BabyPenguin reference infers such fields from their initializer (InferVariableType). Fix: pass 2 infers literal / non-generic-new initializers (with the immutable default mutability of a typed field) and reports E_RESOLVE_TYPE for anything else instead of corrupting the IR. This repro uses the batch-5 shape: a `#class` with an untyped field inside a nested namespace, constructed with `new` and read after the block. Correct behavior asserted: `value=7`, exit 0.
## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3
## Test Code
```
namespace outer {
    namespace cfg {
        #class Cfg {
            value = 7;
        }
    }
}

fun bump(fp: string) -> i64 {
    if (string_length(fp) >= 6) {
        let c0: mut outer.cfg.Cfg = new outer.cfg.Cfg();
        return c0.value;
    }
    return 0;
}

initial {
    let v: i64 = bump("abcdef") + bump("ab");
    let c: mut outer.cfg.Cfg = new outer.cfg.Cfg();
    println("value=" + cast<string>(c.value + v));
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
ExpectedStdout: EQUALS `value=14
`
ExpectedStderr: DISCARD
