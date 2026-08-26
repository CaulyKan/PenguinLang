# TrailingBoolFieldThroughContainer
## Description
RED SENTINEL (known bug, turns green once fixed): a value-type class whose LAST field is a `bool` (after i64/string fields) loses that field's stored value when the instance is pushed into a generic container and read back via `at()` — the bool reads as `true` regardless of what was assigned, while the i64/string fields read back correctly. Minimal shape: `class Item { line: i64; col: i64; text: string; is_comment: bool; }`, a helper `fun` that news the Item and assigns all four fields, `push(make_item(..., false))` then `at(i).is_comment` → prints `true`. Direct (non-container) reads work, so the corruption is in the container store/copy path — likely the struct copy size drops the trailing sub-word field (reads past the copy). Found via the LSP server's formatter (FmtItem preserved-comment flag read back always-true through `_utils.List`); the LSP works around it with text-based comment classification. Correct behavior: both lines print their stored `c=` values — `false` then `true`. Pass3-only (the container is std.Vector, loaded via Compile.Args).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
class Item {
    line: i64 = 0;
    col: i64 = 0;
    text: string = "";
    is_comment: bool = false;

    fun new(mut this) {}
}

fun make_item(line: i64, col: i64, text: string, is_comment: bool) -> mut Item {
    let it: mut Item = new Item();
    it.line = line;
    it.col = col;
    it.text = text;
    it.is_comment = is_comment;
    return it;
}

initial {
    let items : mut std.Vector<Item> = new std.Vector<Item>();
    items.push(make_item(1, 1, "fun", false));
    items.push(make_item(2, 17, "// c1", true));
    let i: mut i64 = 0;
    while (i < items.size()) {
        let it : Item = items.at(cast<u64>(i)).some;
        println(cast<string>(it.line) + ":" + cast<string>(it.col) + " t=" + it.text + " c=" + cast<string>(it.is_comment));
        i = i + 1;
    }
    exit(0);
}
```

## Compile
Args: `EmperorPenguin/std/penguin/vector.penguin`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `1:1 t=fun c=false
2:17 t=// c1 c=true
`
ExpectedStderr: DISCARD
