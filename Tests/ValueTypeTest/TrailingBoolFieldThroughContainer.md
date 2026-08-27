# TrailingBoolFieldThroughContainer
## Description
FIXED (was a red sentinel): a value-type class whose LAST field is a `bool` (after i64/string fields) lost that field's stored value when the instance was pushed into a generic container and read back via `at()` — the bool read as `true` regardless of what was assigned. Root cause: NOT a container/stride bug — `-> mut Item` spells the IR return type "mut ref<Item>", and `is_value_class_ref` only matched the "ref<" prefix, so `needs_sret` was false and the callee returned a bare `ptr` to its own DEAD stack alloca; the caller fed that dangling pointer straight into `push`'s byval slot, and push's frame (same stack addresses) clobbered the struct's last 8 bytes — its `mov %rdi,(%rsp)` spill wrote the Vector `this` pointer there, so the trailing bool read the pointer's low byte (0x28). Fix: is_value_class_ref/get_sret_llvm_type strip the "mut " prefix (LLVMEmitter.penguin), making mutable value-class returns sret (caller-owned buffer) everywhere — i64/string fields survived only because they sat below the callee's frame. Correct behavior (locked in below): both lines print their stored `c=` values. Pass3-only (the container is std.Vector, loaded via Compile.Args).

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
