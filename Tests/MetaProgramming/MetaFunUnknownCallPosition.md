# MetaFunUnknownCallPosition
## Description
An expression/statement-position `#name(args)` whose `#name` has NO registered `#fun` and matches no handled intrinsic (`#sizeof`, `#typeof`, `#compiler`, ...) must be a clean compile error. The old fall-through bound a plain `BoundMetaCallExpression`, but nothing downstream lowers that node — the call (and its value) silently vanished, leaving `x` bound to nothing. Now unit-A binding reports `E_RESOLVE_SYMBOL: unknown meta function '#nosuch_call'` (unit B keeps its documented "unknown -> passthrough" dispatch). Compile must fail with that message. Requires native Pass2/Pass3.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let x: i64 = #nosuch_call(7);
    println(cast<string>(x));
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `unknown meta function '#nosuch_call'`
