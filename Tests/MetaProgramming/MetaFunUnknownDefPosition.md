# MetaFunUnknownDefPosition
## Description
A definition-position `#name(args)` whose `#name` has NO registered `#fun` must be a clean compile error. The parser attaches the trailing definition (the `#name(args) <def>;` annotation form) to the meta call, and the callee `#fun` owns and re-emits it via create_definition — with no `#fun` to run, the old behavior pushed the raw def downstream where nothing consumes it, SILENTLY DROPPING the call and its trailing definition (the field vanished; uses of it then failed with a confusing "has no member" cascade — this is exactly how the win penguin-tools monolith broke when its .penguins omitted argparse.penguin: every `#arg`/`#pos_arg` field silently disappeared). Now the prepass reports `E_RESOLVE_SYMBOL: unknown meta function '#nosuch_meta'`. Compile must fail with that message — not succeed quietly. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Annotated {
    #nosuch_meta("tag")
    tag: string = "";
}
initial {
    let a: mut Annotated = new Annotated();
    println(a.tag);
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `unknown meta function '#nosuch_meta'`
