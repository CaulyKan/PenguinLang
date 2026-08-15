# MetaGenericStaticCall
## Description
The generic-static-call syntax: a generic type name with its args used as the base of a member access — `Box<i64>.json_serialize()` / `Option<i64>.tag()` — resolves to the SPECIALIZED type's static member (including a member injected by a `#specializing` block at pass 3). This is the language fix that lets generated code call static methods on generic types without mangled names (mangled names only live in the symbol/LLVM layer). The `#specializing` block in this file injects `impl IStat` into each `Box<T>` specialization (gated on `T.is_primitive()`), and the static call binds to the injected method on `Box__i64`. Requires native Pass2/Pass3 (meta JIT for the specializing gate).

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
                fun tag() -> string { return "W_stat"; }
            }
        }
    }
}
initial {
    let probe = new __builtin.Box<i64>(0);
    println(Box<i64>.tag());
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
ExpectedStdout: EQUALS `W_stat
`
ExpectedStderr: DISCARD
