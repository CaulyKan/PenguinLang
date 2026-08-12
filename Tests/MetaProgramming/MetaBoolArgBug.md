# MetaBoolArgBug
## Description
RED SENTINEL (unfixed): a `#fun` taking a `bool` parameter mis-evaluates — `#id_b(true)` returns false and `#not_b(false)` returns false (a `!`-in-#fun test). The meta JIT trampoline / bool-arg ABI appears to mis-handle bool arguments (all bool args effectively become false); json.penguin's #funs only pass `type`/`string` args and are unaffected. Intended behavior: `id_false=false`, `id_true=true`, `not_true=false`, `not_false=true`. Should turn green once bool args flow correctly through the meta trampoline. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun id_b(b: bool) -> bool { return b; }
#fun not_b(b: bool) -> bool { return !b; }
initial {
    println("id_false=" + cast<string>(#id_b(false)));
    println("id_true=" + cast<string>(#id_b(true)));
    println("not_true=" + cast<string>(#not_b(true)));
    println("not_false=" + cast<string>(#not_b(false)));
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
ExpectedStdout: EQUALS `id_false=false
id_true=true
not_true=false
not_false=true
`
ExpectedStderr: DISCARD
