# GcNoStackCoverRegionPin

## Description
GC v2 cover-retirement guard for TYPED-REGION elements: the container's element walk must run at its mode-correct point in the minor. In the default REWRITE mode the walk is the LAST root step — after every pin-only source (frame mirrors, quarantine ring) — so elements evacuate and their slots are rewritten; under `EMPEROR_GC_REGION_PINS=1` (pins-only bisect) the walk runs in Phase A, BEFORE the root evacuations. The original ordering bug: the walk sat in Phase D behind the globals/frame-chain evacuations, so an object a container element pointed at got promoted first and the slot kept a frozen tombstone (`buf.push(n)` then reading `buf.at(0)` back after a minor resolves the object's stale pre-evacuation children — the List.at corruption family). The conservative main-stack cover used to mask this (recently-pushed values were also stack-visible and got pinned in Phase A); `EMPEROR_GC_NO_STACK_COVER=1` exposes the ordering bug. `EMPEROR_GC_YOUNG=65536` forces a minor every 64KB so pushes and read-backs cross dozens of polls. The container below mirrors `_utils.List` (`_malloc` buffer + `#__track_buffer` registration + `#__store` element writes).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#template(T: type)
class Buf {
    impl __builtin.IReferenceType;
    buf: mut u64 = 0;
    len: mut u64 = 0;
    cap: mut u64 = 0;

    fun new(mut this) {}

    fun push(mut this, v: T) {
        if (this.len == this.cap) {
            let new_cap: u64 = if (this.cap == 0) { 8 } else { this.cap * 2 };
            let bytes: u64 = new_cap * cast<u64>(#sizeof(T));
            let nb: u64 = __builtin._malloc(bytes);
            #__track_buffer(T, nb, new_cap, cast<u64>(#sizeof(T)));
            let i: mut u64 = 0;
            while (i < this.len) {
                #__store(T, nb + i * cast<u64>(#sizeof(T)), #__load(T, this.buf + i * cast<u64>(#sizeof(T))));
                i = i + 1;
            }
            if (this.buf != 0) {
                __builtin._gc_scan_remove(this.buf);
                __builtin._mfree(this.buf);
            }
            this.buf = nb;
            this.cap = new_cap;
        }
        #__store(T, this.buf + this.len * cast<u64>(#sizeof(T)), v);
        this.len = this.len + 1;
    }

    fun at(this, i: u64) -> __builtin.Option<T> {
        if (i >= this.len) { return new __builtin.Option<T>.none(); }
        return new __builtin.Option<T>.some(#__load(T, this.buf + i * cast<u64>(#sizeof(T))));
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
Env: `EMPEROR_GC_MODE=precise EMPEROR_GC_NO_STACK_COVER=1 EMPEROR_GC_YOUNG=65536`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `acc=33711
`
ExpectedStderr: DISCARD
