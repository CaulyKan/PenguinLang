# PreciseRefMap
## Description
Precise-GC ref-map regression: heap objects combining every layout the ref-map
encoder must describe — an inline value class (Inner embedded in Outer, with a
string field inside), a bare string field, and an enum field with both a
value-class payload variant and a bare-pointer payload variant. Heavy
StringBuilder/string churn between reads forces many collections while the
graph stays reachable ONLY through precise-map slots (the conservative body
scan no longer backs these objects up). Green = the emitted ref-maps keep the
graph alive and the reads see intact data.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Inner {
    name: string = "";
    v: i64 = 0;
    impl __builtin.ICopy<Inner> for Inner;
}
class Outer {
    inner: Inner = new Inner();
    tag: i64 = 0;
    s: string = "";
}
enum Shape { point; circle: Inner; label: string; }
class Holder {
    sh: mut Shape = new Shape.point();
    list: string = "";
}
initial {
    let o: mut Outer = new Outer();
    o.inner.name = "abc";
    o.inner.v = 42;
    o.s = "hello";
    let h: mut Holder = new Holder();
    h.sh = new Shape.circle(new Inner());
    let acc: mut StringBuilder = new StringBuilder();
    let i: mut i64 = 0;
    while (i < 200000) {
        acc = new StringBuilder();
        acc.append(o.s);
        acc.append(cast<string>(i));
        let tmp: string = acc.to_string();
        if (string_length(tmp) == 0) { println("bad"); }
        i = i + 1;
    }
    println(o.inner.name + o.s + h.list);
    gc_collect();
    println(o.inner.name + o.s + h.list);
    h.sh = new Shape.label("tagged");
    gc_collect();
    println(o.inner.name + h.list);
}
```

## Run
ExpectedExitCode: 0
ExpectedStdout: EQUALS `abchello
abchello
abc
`
