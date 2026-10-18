# GcNoStackCoverRegionPin

## Description
Native-array element guard (the "NoStackCover" name is historical): a container's `array<T>` element storage must be traced precisely on every collection, so an element's target stays live from `buf.push(n)` until every later `buf.at(...)` read-back. The historical failure shape: the element walk ran at the wrong point in the cycle, so an object a container element pointed at was judged through stale state (`buf.push(n)` then reading `buf.at(0)` back after a collection resolved the object's stale pre-collection children — the List.at corruption family); the conservative main-stack cover used to mask this (recently-pushed values were also stack-visible). `EMPEROR_GC_YOUNG=65536` lowers the goal's churn slack so collections fire every ~64KB and pushes and read-backs cross dozens of them. The container below mirrors `_utils.List`'s native-array design (`__array_new` grow + `__array_copy_range` copy + `__array_set` writes).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class Buf {
    impl __builtin.IReferenceType;
    buf: mut array<T>;
    len: mut u64 = 0;
    cap: mut u64 = 0;

    fun new(mut this) {}

    fun push(mut this, v: T) {
        if (this.len == this.cap) {
            let new_cap: u64 = if (this.cap == 0) { 8 } else { this.cap * 2 };
            let nb: mut array<T> = __builtin.__array_new<T>(new_cap);
            __builtin.__array_copy_range<T>(nb, this.buf, this.len);
            this.buf = nb;
            this.cap = new_cap;
        }
        __builtin.__array_set<T>(this.buf, this.len, v);
        this.len = this.len + 1;
    }

    fun at(this, i: u64) -> __builtin.Option<T> {
        if (i >= this.len) { return new __builtin.Option<T>.none(); }
        return new __builtin.Option<T>.some(__builtin.__array_get<T>(this.buf, i));
    }

    fun size(this) -> u64 { return this.len; }
}
class Node {
    impl __builtin.IReferenceType;
    v: i64;
    s: string;
    fun new(mut this, v: i64, s: string) { this.v = v; this.s = s; }
}
fun noise(seed: i64) -> string {
    let sb: mut StringBuilder = new StringBuilder();
    let i: mut i64 = 0;
    while (i < 10) {
        sb.append(cast<string>((seed * 7 + i * 3) % 97));
        i = i + 1;
    }
    return sb.to_string();
}
initial {
    let buf: mut Buf<Node> = new Buf<Node>();
    let acc: mut i64 = 0;
    let i: mut i64 = 0;
    while (i < 240) {
        let n = new Node(i, noise(i));
        buf.push(n);
        let junk: string = noise(i) + cast<string>(string_length(buf.at(cast<u64>(0)).some.s));
        acc = acc + buf.at(cast<u64>(0)).some.v + n.v + string_length(junk);
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
Env: `EMPEROR_GC_YOUNG=65536`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `acc=33711
`
ExpectedStderr: DISCARD
