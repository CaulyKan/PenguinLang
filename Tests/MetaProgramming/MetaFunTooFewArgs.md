# MetaFunTooFewArgs
## Description
A definition-position `#fun` call with FEWER arguments than the #fun declares (`#g();` against `#fun g(field: string) -> ast`) must be a clean compile error. Previously the JIT caller-stub baked only the given arguments, so the callee read a garbage string pointer and the compiler SEGFAULTED (exit 139, verified on Pass2/Pass3 at 2026-09-13); fixed in SemanticBindMetaCalls.bind_meta_args with an explicit too-few-arguments check (a trailing `{ ... }` block still legitimately fills an unstructured_ast last parameter). Compile must now fail with E_UNSUPPORTED and exit 1 — not crash.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun g(field: string) -> ast {
    return compiler().create_definition("fun _u_" + field + "() -> i64 { return 1; }");
}
#g();
initial { println("x"); }
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `too few arguments`
