# GcInterfaceTreeUnbox
## Description
GC v2 receiver/unbox-idiom smoke: builds 300 expression trees (depth 30) of interface-typed nodes (`IE`), where every method body down-casts its immutable receiver via `cast<LitExpr>(this)` — the unbox alias-view idiom. The tree is held through `Option<IE>` fields and traversed after construction, so every old→young edge (write barrier), every boxed receiver slot and every Option payload must survive the tiny-nursery minors. This was the shape of the 3c cover-retirement receiver crash (poll-before-read, promotion-then-tombstone).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
interface IE { impl __builtin.IReferenceType; fun build_text(this: IE) -> string; }
class LitExpr {
    value: mut string = "";
    fun new(mut this, value: string) { this.value = value; }
    impl __builtin.IReferenceType {}
    impl IE {
        fun build_text(this: IE) -> string {
            let self = cast<LitExpr>(this);
            return self.value;
        }
    }
}
class BinExpr {
    op: mut string = "";
    l: mut Option<IE> = new Option<IE>.none();
    r: mut Option<IE> = new Option<IE>.none();
    fun new(mut this, op: string, l: IE, r: IE) {
        this.op = op;
        this.l = new Option<IE>.some(l);
        this.r = new Option<IE>.some(r);
    }
    impl __builtin.IReferenceType {}
    impl IE {
        fun build_text(this: IE) -> string {
            let self = cast<BinExpr>(this);
            return self.l.some.build_text() + self.op + self.r.some.build_text();
        }
    }
}
fun mk(depth: i64) -> IE {
    let e: mut IE = new LitExpr("x");
    let i: mut i64 = 0;
    while (i < depth) {
        e = new BinExpr("+", e, new LitExpr("1"));
        i = i + 1;
    }
    return e;
}
initial {
    let acc: mut string = "";
    let k: mut i64 = 0;
    while (k < 300) {
        let t = mk(30);
        acc = acc + t.build_text();
        k = k + 1;
    }
    println("len=" + cast<string>(string_length(acc)) + " head=" + string_substring(acc, 0, 10));
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
Env: `EMPEROR_GC_MODE=precise EMPEROR_GC_YOUNG=65536`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `len=18300 head=x+1+1+1+1+
`
ExpectedStderr: DISCARD
