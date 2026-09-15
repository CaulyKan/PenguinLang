# LateWireDefaultMethodVtable
## Description
A channel whose ONLY LatestChannel<T> specialization is created by connect desugaring (output port -> input port: the granted wire's spec is built on demand during pass-8 binding, not by the pass-3 fixpoint) must still get correct vtables for INTERFACE DEFAULT METHODS. The consumer's `wait this.din` dispatches do_wait virtually through the wire object's IFuture vtable; pre-fix, that slot fell back to the unemitted template method (link error: undefined `@__builtin_IFuture_do_wait`) because the late specialization path did not close the `impl IFuture<T>` interface-specification closure the way the pass-3 fixpoint does (finish_late_spec_def now ensures every impl edge's interface specialization BEFORE the catch-up builds vtables). Payload is a user VALUE class, and no explicit LatestChannel<Msg> annotation exists anywhere — the wire spec is purely connect-generated. 34 = a*10+b proves the writeback of both fields.

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Msg {
    impl ICopy<Self>;
    a: i64;
    b: i64;
}

class Producer {
    output out : Msg;
    initial {
        let m: mut Msg = new Msg();
        m.a = 3;
        m.b = 4;
        this.out.write(m);
    }
}

class Consumer {
    input din : Msg;
    initial {
        let v: Msg = wait this.din;
        println(cast<string>(v.a * 10 + v.b));
    }
}

construct {
    let p: mut Producer = new Producer();
    let c: mut Consumer = new Consumer();
    connect(p.out, c.din);
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `34
`
ExpectedStderr: DISCARD
