# ListVectorOps
## Description
_utils.List under the contiguous-buffer (vector) implementation: growth across
realloc boundaries (initial cap 8, doubling), bounds-checked at/set, remove from
head/middle, pop, for-in summation, and value semantics for value-type-class
elements. Slots store bare T (loaded/stored via #__load/#__store), so an
immutable value-type element is an inline struct: at() and iter_mut hand out
copies, and mutating them (e.bump(), x.bump() in the loop) never writes back
into the slot — alias stays 1,2. Use List<mut T> slots for write-back aliasing.
Verified on all compilers.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class P { v: mut i64 = 0; fun new(mut this, x: i64) { this.v = x; } fun get(this) -> i64 { return this.v; } fun bump(mut this) { this.v = this.v + 100; } }
    initial {
        let a: mut _utils.List<i64> = new _utils.List<i64>();
        let i: mut i64 = 0;
        while (i < 40) { a.push(i * 2); i = i + 1; }
        println("size=" + cast<string>(a.size()));
        println("a0=" + cast<string>(a.at(0).some));
        println("a8=" + cast<string>(a.at(8).some));
        println("a39=" + cast<string>(a.at(39).some));
        println("oob=" + cast<string>(a.at(40).is_none()));
        a.set(8, 123);
        println("set=" + cast<string>(a.at(8).some));
        a.remove(0);
        println("rm0=" + cast<string>(a.at(0).some));
        a.remove(7);
        println("rm123=" + cast<string>(a.at(7).some));
        let p: __builtin.Option<i64> = a.pop();
        println("pop=" + cast<string>(p.some));
        let s: mut i64 = 0;
        for (let v in a) { s = s + v; }
        println("sum=" + cast<string>(s));
        let r: mut _utils.List<P> = new _utils.List<P>();
        r.push(new P(1));
        r.push(new P(2));
        let e: mut P = r.at(0).some;
        e.bump();
        for (let x : mut P in r) { x.bump(); }
        println("alias=" + cast<string>(r.at(0).some.get()) + "," + cast<string>(r.at(1).some.get()));
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `size=40
a0=0
a8=16
a39=78
oob=true
set=123
rm0=2
rm123=18
pop=78
sum=1466
alias=1,2
`
ExpectedStderr: DISCARD
