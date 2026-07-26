# MetaIfInNamespace
## Description
Phase 5a.4: def-level meta resolves inside namespaces too. `ns.val()` is included by `#if (true)` and called from the initial block.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace ns {
    #if (true) {
        fun val() -> i64 { return 5; }
    }
}
initial {
    println(cast<string>(ns.val()));
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
ExpectedStdout: EQUALS `5
`
ExpectedStderr: DISCARD
