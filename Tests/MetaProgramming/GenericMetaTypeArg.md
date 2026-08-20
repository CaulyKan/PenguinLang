# GenericMetaTypeArg
## Description
The #sizeof / #__load / #__store pointer intrinsics accept a GENERIC type
operand (e.g. #sizeof(Option<T>), #__store(Option<T>, addr, v)): the bare name
resolves to the generic definition, then the compiler specializes it by the
written arguments (mangle + specialization-symbol lookup, creating the
specialization on demand when every argument is concrete) so the load/store
uses the real specialized enum layout and #sizeof yields its true byte size.
Option<i32> lays out as { ptr meta, i64 tag, [4 x i8] payload } — the payload
union is an opaque byte array (so whole-enum copies can never be split at a
single variant's field boundaries) and the tag is i64 (so the alignment-1
byte array lands at offset 16); total = align_up(16+4, 8) = 24. This is the
mechanism the contiguous _utils.List uses for its T slots. BabyPenguin's
ANTLR grammar does not parse expression-level meta calls, so Apply To is
EmperorPenguin-only. Verified on Pass1/Pass2/Pass3.

## Apply To
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class SlotBox {
    buf: mut u64 = 0;
    fun new(mut this) {
        this.buf = __builtin._malloc(cast<u64>(#sizeof(Option<T>)));
    }
    fun put(mut this, v: T) {
        #__store(Option<T>, this.buf, new Option<T>.some(v));
    }
    fun get(this) -> Option<T> {
        return #__load(Option<T>, this.buf);
    }
}
initial {
    let n: i64 = #sizeof(Option<i32>);
    println("opt=" + cast<string>(n));
    let b: mut SlotBox<i64> = new SlotBox<i64>();
    b.put(41);
    let v: Option<i64> = b.get();
    if (v.is_some()) { println("v=" + cast<string>(v.some + 1)); }
    let b2: mut SlotBox<string> = new SlotBox<string>();
    b2.put("hi");
    println("s=" + b2.get().some);
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
ExpectedStdout: EQUALS `opt=24
v=42
s=hi
`
ExpectedStderr: DISCARD
