# GcNoStackCoverWriteChain

## Description
GC v2 cover-retirement guard for WRITE-CHAIN / alias-assign / unbox registers: a chain temp (`o.mid.inner.v = f(x)`, a mut-this receiver `o.mid.inner.bump(f(x))`, a value-class interior chain `h.p.x = f(x)`, a ref-global chain `g.f.x = f(x)`, an unbox alias view) exists only as an SSA/gep value between its def and the consuming WRMBR/call — every poll in that window (argument evaluation) moves the target unless something pins it. The emitter homes these registers as write-only PIN mirror slots (`%reg.gcpin` + the `_emperor_gc_pin_refmap` sentinel): the minor's Phase-A pin pass resolves the mirror (interior-tolerant — an inline-struct chain's gep pins its OWNER) before any promotion, so the SSA copy never dangles into a recycled chunk. With `EMPEROR_GC_NO_STACK_COVER=1` and `EMPEROR_GC_YOUNG=65536` the chain temps cross dozens of un-covered minors; a missing mirror reads a recycled chunk (silent corruption or SIGSEGV).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace t {
    class Inner {
        impl __builtin.IReferenceType;
        v: mut i64 = 0;
        s: mut string = "";
        fun new(mut this, v: i64, s: string) { this.v = v; this.s = s; }
        fun bump(mut this, d: i64) -> i64 { this.v = this.v + d; return this.v; }
    }
    class Mid {
        impl __builtin.IReferenceType;
        inner: mut Inner = new Inner(0, "");
        tag: mut string = "";
        fun new(mut this, inner: Inner, tag: string) { this.inner = inner; this.tag = tag; }
    }
    class Outer {
        impl __builtin.IReferenceType;
        mid: mut Mid = new Mid(new Inner(0, ""), "");
        fun new(mut this, mid: Mid) { this.mid = mid; }
    }
    class Point {
        impl __builtin.IValueType;
        x: mut i64 = 0;
        y: mut i64 = 0;
        fun new(mut this, x: i64, y: i64) { this.x = x; this.y = y; }
    }
    class Holder {
        impl __builtin.IValueType;
        p: mut Point = new Point(0, 0);
        label: string = "";
        fun new(mut this, p: Point, label: string) { this.p = p; this.label = label; }
    }
    fun noise(seed: i64) -> string {
        let s: mut StringBuilder = new StringBuilder();
        let i: mut i64 = 0;
        while (i < 8) {
            s.append(cast<string>((seed + i) % 97));
            i = i + 1;
        }
        return s.to_string();
    }
    fun churn_outer(o: mut Outer, seed: i64) -> i64 {
        // ref-field write chain with a call in the RHS
        o.mid.inner.v = string_length(noise(seed)) + 1;
        // mut-this RECEIVER through a ref chain, call in the args
        let r1: i64 = o.mid.inner.bump(string_length(noise(seed + 1)));
        o.mid.inner.s = noise(seed + 2);
        let r2: i64 = string_length(o.mid.inner.s);
        return r1 + r2 + o.mid.inner.v;
    }
    fun churn_holder(h: mut Holder, seed: i64) -> i64 {
        // VALUE-CLASS inline chain: interior gep across polls
        h.p.x = string_length(noise(seed)) + 2;
        h.p.y = h.p.x + string_length(noise(seed + 1));
        return h.p.x + h.p.y + string_length(h.label);
    }
    fun enum_chain(e: mut Option<i64>, seed: i64) -> i64 {
        if (e.is_some()) {
            return e.value_or(0) + string_length(noise(seed));
        }
        return string_length(noise(seed));
    }
}
let gmid: mut t.Mid = new t.Mid(new t.Inner(1, "g"), "gm");
fun global_chain(seed: i64) -> i64 {
    // REF-GLOBAL write chain across the RHS poll
    gmid.inner.v = string_length(t.noise(seed)) + 3;
    return gmid.inner.v;
}
initial {
    let acc: mut i64 = 0;
    let eo: mut t.Option<i64> = new t.Option<i64>.some(5);
    let i: mut i64 = 0;
    while (i < 2000) {
        let o = new t.Outer(new t.Mid(new t.Inner(i, "seed-" + cast<string>(i)), "m" + cast<string>(i)));
        acc = acc + t.churn_outer(o, i);
        let h: mut t.Holder = new t.Holder(new t.Point(i, i * 2), "h" + cast<string>(i));
        acc = acc + t.churn_holder(h, i + 7);
        acc = acc + t.enum_chain(eo, i);
        acc = acc + global_chain(i);
        i = i + 1;
    }
    println("acc=" + cast<string>(acc));
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
Env: `EMPEROR_GC_MODE=precise EMPEROR_GC_NO_STACK_COVER=1 EMPEROR_GC_YOUNG=65536`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `acc=340543
`
ExpectedStderr: DISCARD
