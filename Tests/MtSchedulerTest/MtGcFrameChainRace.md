# MtGcFrameChainRace
## Description
mt kernel + frequent global GC corrupts the precise frame-chain walk: 4
handlers churn small chains, rotate a LONG-LIVED `array<u8>` frame local
(reassigned every 8th round), and push msgs through a shared Fifo to one
logger. Under EMPEROR_THREADS>=4 with a tight young budget the run dies
two ways: (a) exit 134 — `emperor gc: CORRUPT REF-MAP (index out of
bounds) in type gcframe ... ctx=frame-mark` (a frame descriptor's ref-map
pointer read as a coroutine-stack address), or (b) exit 63 — the
scheduler's lost-wake detector aborts (`co=N state=COND ... at=NOWHERE`).
RED SENTINEL on Pass2/Pass3 (mt is native-only; BabyPenguin/Pass1 cannot
run real threads).

ROOT CAUSE OF (a), found round 19 (2026-10-23): the LLVM emitter decides
whether a penguin function links a GC frame descriptor at scan time
(`gc_frame_predicted`), but frame slots are also added later — by the
entry parameter setup and by struct temps during body emission. Functions
taking a native `array<T>` parameter (e.g. the `array_len/get/fill`
builtins the buffer code calls) were therefore emitted with a descriptor
on entry and WITHOUT the ret-side unlink: every call leaked a dead
descriptor onto `_emperor_gc_frame_head`. The next function to reuse that
stack memory overwrote it, and the next collection walked the garbage.
Fix: the emitter emits the unlink unconditionally and, once the body is
emitted, strips it for functions that turned out to have no descriptor
(`emit_gc_frame_descriptor` -> bool + `strip_gc_frame_unlinks`). Verified
A/B: tbm_large mt8 6/6 clean after vs 2/6 CORRUPT before; the 134 mode is
gone in 24/24 sentinel runs.

Mode (b) ROOT-CAUSED AND FIXED (2026-10-23, round 21): the GC valve list
(mt_gc_parked — the STW-escalation / hard-cap park) shared EmperorCoroutine
.qnext with the RUN QUEUES. A coroutine parked on the valve mid-do_wait is
ALSO registered in the futures set; the delivery kick that woke it pushed
a run-queue entry, CLOBBERING qnext and truncating the valve chain — every
coroutine below the stale entry was never released by the boundary: parked
CO_COND, in no list (the "at=NOWHERE" dumps), a lost wake. GC-off was
clean because the valve never engages. Fix: a dedicated gcnext link field
for the valve + mt_valve_unlink_locked on wake and on re-park
(scheduler.c). Verified: 32/32 clean over all four Run sections (plain +
three shuffle seeds), 8 threads, tight young budget; MtSchedulerTest
64/64. Forensics trail: EMPEROR_VTRACE=1 traces every valve park/release
and flags wakes of valve-linked coroutines.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Node {
    impl __builtin.IReferenceType;
    v: mut i64 = 0;
    n1: mut __builtin.Option<Node> = new __builtin.Option<Node>.none();
}
class Msg {
    impl __builtin.IReferenceType;
    from: mut i64 = 0;
}
fun chain(base: i64, n: i64) -> mut Node {
    let head: mut Node = new Node();
    head.v = base;
    let cur: mut Node = head;
    let i: mut i64 = 1;
    while (i < n) {
        let x: mut Node = new Node();
        x.v = base + i;
        cur.n1 = new __builtin.Option<Node>.some(x);
        cur = x;
        i = i + 1;
    }
    return head;
}
fun chain_sum(h: mut Node) -> i64 {
    let acc: mut i64 = 0;
    let cur: mut Node = h;
    let alive: mut bool = true;
    while (alive) {
        acc = acc + cur.v;
        if (cur.n1.is_some()) { cur = cast<mut Node>(cur.n1.some); } else { alive = false; }
    }
    return acc;
}
fun logger(total: i64, ch: mut __builtin.Fifo<mut Msg>) -> i64 {
    let got: mut i64 = 0;
    let acc: mut i64 = 0;
    while (got < total) {
        let m: mut Msg = wait ch;
        acc = acc + m.from;
        got = got + 1;
    }
    return acc;
}
fun handler(id: i64, rounds: i64, log: mut __builtin.Fifo<mut Msg>) -> i64 {
    let i: mut i64 = 0;
    let acc: mut i64 = 0;
    let c8: mut i64 = 0;
    let c16: mut i64 = 0;
    let buf: mut array<u8> = create_array(65536, cast<u8>(7));
    while (i < rounds) {
        acc = acc + chain_sum(chain(id * 100000000 + i, 3));
        c8 = c8 + 1;
        if (c8 == 8) {
            c8 = 0;
            buf = create_array(65536, cast<u8>(5));
            acc = acc + cast<i64>(buf.at(cast<u64>(100)).value_or(0));
        }
        c16 = c16 + 1;
        if (c16 == 16) {
            c16 = 0;
            let m: mut Msg = new Msg();
            m.from = id;
            log.write(m);
        }
        i = i + 1;
    }
    return acc;
}
initial {
    let log: mut __builtin.Fifo<mut Msg> = new __builtin.Fifo<mut Msg>(64, new __builtin.FifoPolicy.backpressure());
    let R: i64 = 120000;
    let lg = async logger(R * 4 / 16, log);
    let h0 = async handler(0, R, log);
    let h1 = async handler(1, R, log);
    let h2 = async handler(2, R, log);
    let h3 = async handler(3, R, log);
    let s0: i64 = wait h0;
    let s1: i64 = wait h1;
    let s2: i64 = wait h2;
    let s3: i64 = wait h3;
    let sl: i64 = wait lg;
    println("checksum=" + cast<string>(s0 + s1 + s2 + s3 + sl));
}
```

## Compile
Args: `--enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run 1
Args: ``
Env: `EMPEROR_THREADS=8 EMPEROR_GC_YOUNG=8388608`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `checksum=216086401065000
`
ExpectedStderr: DISCARD

## Run 2
Args: ``
Env: `EMPEROR_THREADS=8 EMPEROR_GC_YOUNG=8388608 EMPEROR_SHUFFLE_DELTAS=1`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `checksum=216086401065000
`
ExpectedStderr: DISCARD

## Run 3
Args: ``
Env: `EMPEROR_THREADS=8 EMPEROR_GC_YOUNG=8388608 EMPEROR_SHUFFLE_DELTAS=7`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `checksum=216086401065000
`
ExpectedStderr: DISCARD

## Run 4
Args: ``
Env: `EMPEROR_THREADS=8 EMPEROR_GC_YOUNG=8388608 EMPEROR_SHUFFLE_DELTAS=42`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `checksum=216086401065000
`
ExpectedStderr: DISCARD
