# PortSupportClasses
## Description
Exercises the stdlib support classes behind the RTL port lowering, on both compilers' channel layers: ConstantSource (an input port's declared default — current() yields the constant, try_poll never delivers), _Fanout (an output port's hub — set_seed is a weak deliverable seed so both subscribers' first wait wakes with it, then every write reaches each subscriber independently), and _LateSource (the passed-through-input relay — bind() forwards poll to the bound hub; the hub's slot stream delivers its own pending transactions in FIFO order, so the first take after seed+write(11)+write(99) is 11, then 99).

Also a green regression lock for two EmperorPenguin compiler bugs found while porting these classes (both fixed): (1) a member call on a SPECIALIZED generic class resolving through the template's impl-method symbol returned the unresolved Option<T>-shaped type (call site emitted `call i64` against an sret definition — clang "invalid cast opcode"); the specialized def's interface_impls methods are now authoritative for the return type. (2) a generic `new Foo<T>()` appearing only as a field default of an interface-typed field in a specialized class never got its specialization def created (no layout — the NEW lowered to an undefined register); bind_new_expr now ensures the specialization on demand.

GREEN since 2026-08-27 (was a RED sentinel on BabyPenguin): directly constructing `__builtin._LateSource<i64>` from user code used to fail BabyPenguin with E_TYPE_MISMATCH at its own Builtin.penguin field default (`upstream : mut IChannel<T> = new _NeverSource<T>()`). Two BabyPenguin cast-check gaps were fixed: (a) class/enum/interface → interface implicit casts compared implemented interfaces by FullName STRING, so mutability-flavored generic args (`IChannel<!mut i64>` vs `IChannel<i64>`) never matched — now structural and mutability-insensitive anywhere (BabyPenguin/Type/TypeStructure.cs, matching EmperorPenguin's is_same_type which never compares mutability); (b) a specialization created mid-pass-04 (by a field default) has no vtables until pass 05, so the cast check saw an empty impl list — the check now also consults the class's declared `impl` blocks resolved in its scope (same resolution the vtable build would perform later).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
initial {
    let cs : mut __builtin.ConstantSource<i64> = new __builtin.ConstantSource<i64>(42);
    let c : __builtin.Option<i64> = cs.current();
    println("cs.current=" + cast<string>(c.some));

    let hub : mut __builtin._Fanout<i64> = new __builtin._Fanout<i64>();
    hub.set_seed(7, true);
    let w1 : mut __builtin.LatestChannel<i64> = hub.subscribe();
    let w2 : mut __builtin.LatestChannel<i64> = hub.subscribe();
    let s1 : i64 = wait w1;
    let s2 : i64 = wait w2;
    println("seeds=" + cast<string>(s1) + "," + cast<string>(s2));
    cast<mut __builtin.ISink<i64>>(hub).write(11);
    let v1 : i64 = wait w1;
    let v2 : i64 = wait w2;
    println("fanout=" + cast<string>(v1) + "," + cast<string>(v2));

    let late : mut __builtin._LateSource<i64> = new __builtin._LateSource<i64>();
    late.bind(cast<mut __builtin.IChannel<i64>>(hub));
    let lv : i64 = wait late;
    println("late=" + cast<string>(lv));

    let ch : mut __builtin.LatestChannel<i64> = new __builtin.LatestChannel<i64>();
    cast<mut __builtin.ISink<i64>>(ch).write(5);
    let cur : __builtin.Option<i64> = ch.current();
    println("chan.current=" + cast<string>(cur.some));
    let pw : i64 = wait ch;
    println("chan.wait=" + cast<string>(pw));
    exit(0);
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `cs.current=42
seeds=7,7
fanout=11,11
late=11
chan.current=5
chan.wait=5
`
ExpectedStderr: DISCARD
