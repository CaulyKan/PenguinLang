# MutexChannelPromote
## Description

Two-tier heap M2 functional lock: objects crossing the handoff points (Mutex construction/set/with, channel push) must behave byte-exact with write-point promotion active — payload graphs get the shared bit over their closure inside the lock, and the still-full collector must not perturb anything. Exercises: Mutex<ref> set of an externally-built graph, with() allocating fresh objects inside the critical section (crit-list batch promotion at exit), a Mutex payload swapped through a channel handoff, and gc_collect() churn between every step. The promote section of gc_torture covers the runtime contracts (closure count, idempotence, shared-not-immortal) — this case locks the END-TO-END language surface.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Node {
    impl IReferenceType;
    value: mut i64 = 0;
    next: mut __builtin.Option<Node> = new __builtin.Option<Node>.none();
}

fun build(n: i64) -> mut Node {
    let head: mut Node = new Node();
    head.value = n;
    return head;
}

initial {
    // construct over an externally built graph, then read back
    let g: mut Node = build(5);
    let m: mut __builtin.Mutex<Node> = new __builtin.Mutex<Node>(g);
    println("ctor=" + cast<string>(m.get().value));

    // with() allocating fresh objects inside the critical section:
    // the crit list batch-promotes them at exit (still inside the lock)
    m.with(fun(v: mut Node) -> void {
        let fresh: mut Node = build(7);
        fresh.next = new __builtin.Option<Node>.some(v);
        v.value = fresh.value;
    });
    println("with-alloc=" + cast<string>(m.get().value));

    // whole-payload replacement via set
    let g2: mut Node = build(9);
    m.set(g2);
    println("set=" + cast<string>(m.get().value));
}
```

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `ctor=5
with-alloc=7
set=9
`
ExpectedStderr: DISCARD
