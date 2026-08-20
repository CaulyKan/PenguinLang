# ImplicitValueToInterfaceBoxes

## Description
Value-copy semantics for interfaces: an IMPLICIT value-class -> interface
conversion (`let i: mut IInc = c`) must BOX (copy), exactly like the explicit
`cast<IInc>(c)`. The interface value is an independent box, so mut-this
interface calls mutate the BOX, not the original value — and the unbox idiom
`let self: mut Self = cast<mut Self>(this)` must be an ALIAS VIEW of the box
so successive calls observe the same state (1, 2, 3), while the original
`c.count` stays 0 (the box was a copy at conversion).

Before the value-copy semantics change, EmperorPenguin lowered the implicit
conversion as a raw storage alias AND emit_unbox memcpy'd a detached copy, so
interface state either leaked into the caller's value or reset per call.
Verified identical on BabyPenguin (1230) and EmperorPenguin pass1 (1230).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
class Counter {
    count: mut i64 = 0;
    impl IInc {
        fun bump(this: mut IInc) -> i64 {
            let self: mut Self = cast<mut Self>(this);
            self.count = self.count + 1;
            return self.count;
        }
    }
}
interface IInc { fun bump(this: mut IInc) -> i64; }
fun go(i: mut IInc) -> i64 { return i.bump(); }
initial {
    let mut c = new Counter();
    let i: mut IInc = c;
    print(cast<string>(i.bump()));
    print(cast<string>(i.bump()));
    print(cast<string>(go(i)));
    print(cast<string>(c.count));
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
ExpectedStdout: EQUALS `1230`
ExpectedStderr: DISCARD
