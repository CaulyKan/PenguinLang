# ArgparseKeywordAnnotationHangs
## Description
Keyword-style `#arg` annotation arguments must be rejected with a parse error instead of wedging the compiler. `#arg(help: "h", short: "s", long: "l", required: false, default_text: "d")` (name-colon-value form) before a class field used to spin every EmperorPenguin pass at 100% CPU forever — no diagnostic, no exit (>240s verified before the fix). Root cause: the `#name(...)` argument loops never checked progress — the argument parsers return a constant WITHOUT consuming on unrecognized tokens (parse_primaryExpression's fallback), so the loop wedged at the `:` of a named argument, pushing empty arguments forever (the earlier `#typeof(i32)` keyword hang was the same zero-progress shape, special-cased per keyword). Fixed in Parser.penguin with the shared `parse_meta_call_arguments` loop: an `identifier` directly followed by `:` is diagnosed as "named arguments are not supported", every zero-progress iteration is reported and force-stepped, and an unterminated list at EOF ends the loop. Each named argument yields two E_PARSE diagnostics; the compile exits non-zero fast. This test asserts that fixed behavior (turns red again if the loop can hang or silently accept keyword args).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Opts {
    #arg(help: "h", short: "s", long: "l", required: false, default_text: "d")
    greet: string = "world";
}

initial { println("done"); }
```

## Compile
Args: `EmperorPenguin/std/penguin/argparse.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_PARSE: named arguments are not supported in #arg(...)`
