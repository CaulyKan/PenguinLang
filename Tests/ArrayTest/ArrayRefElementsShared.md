# ArrayRefElementsShared
## Description
Reference-class elements share identity through the array slot: set stores the reference (pointer copy), later mutation through the original binding is visible via at() on the same slot. (Note: `at(i)` yields an immutable `T` binding — in-place mutation through the read is not part of this round's surface; use set() to replace slots.)

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Counter {
    impl IReferenceType;
    n: mut i64;
    fun new(mut this) {
        this.n = 0;
    }
    fun bump(mut this) {
        this.n += 1;
    }
}
initial {
    // reference fill: every slot holds the SAME object reference
    let shared : mut Counter = new Counter();
    let cs : mut array<Counter> = create_array(2, shared);
    shared.bump();
    shared.bump();
    println("c0=" + cast<string>(cs.at(0).some.n));
    println("c1=" + cast<string>(cs.at(1).some.n));

    // set stores the reference: mutation through the binding stays visible
    let c : mut Counter = new Counter();
    cs.set(1, c);
    c.bump();
    c.bump();
    c.bump();
    println("c1b=" + cast<string>(cs.at(1).some.n));
    println("c0still=" + cast<string>(cs.at(0).some.n));
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
ExpectedStdout: EQUALS `c0=2
c1=2
c1b=3
c0still=2
`
ExpectedStderr: DISCARD
