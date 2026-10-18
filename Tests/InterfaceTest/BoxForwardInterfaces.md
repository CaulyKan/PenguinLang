# BoxForwardInterfaces
## Description
Box interface forwarding (primitive interfaces round): a Box<T> specialization automatically re-implements every interface T implements (primitives' IHash/IStringOps, user `impl IGreet for i64`, a class's impls, and an inner Box's forwarded impls for nested boxes) by delegating to `this.value`. Direct calls, true interface-typed polymorphism (vtable dispatch through an IHash variable), and the static `is` all work.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
interface IGreet { fun greet(this) -> string; }
impl IGreet for i64 { fun greet(this) -> string { return "i64:" + cast<string>(this); } }

interface IVal { fun v(this) -> i64; }
class V {
    x: i64;
    fun new(mut this, x: i64) { this.x = x; }
    impl IReferenceType;
    impl IVal { fun v(this) -> i64 { return this.x; } }
}

initial {
    let a: mut Box<i64> = new Box<i64>(7);
    println(cast<string>(a.hash()));
    let b: IHash = cast<IHash>(a);
    println(cast<string>(b.hash()));
    let s: mut Box<string> = new Box<string>("hello");
    println(cast<string>(s.length()));
    println(cast<string>(s is IStringOps));
    let n: mut Box<Box<i64>> = new Box<Box<i64>>(new Box<i64>(9));
    println(cast<string>(n.hash()));
    let g: mut Box<i64> = new Box<i64>(42);
    println(cast<string>(g.greet()));
    let bv: mut Box<V> = new Box<V>(new V(3));
    println(cast<string>(bv.v()));
    let iv: IVal = cast<IVal>(bv);
    println(cast<string>(iv.v()));
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
ExpectedStdout: EQUALS `7
7
5
true
9
i64:42
3
3
`
ExpectedStderr: DISCARD
