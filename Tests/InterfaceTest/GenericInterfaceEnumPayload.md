# GenericInterfaceEnumPayload
## Description
A generic enum's payload must survive the round-trip through a generic interface's virtual call: `impl IBox<i32>` returns `mut Wrap<i32>` from `get()` (an enum return via sret through CALL_VIRT), and the payload field `w.w` reads back 7 on every compiler.

History: this test replaces `GenericInterfaceEnumReturnLost`, which (mis)diagnosed EP printing `0` for `cast<string>(w)` as a lost enum payload through generic-interface dispatch. The payload path was verified fine; `0` is EmperorPenguin's documented enum→string semantics (tag ordinal — see EnumTest/EnumCastToString, which asserts that behavior deliberately), while the BabyPenguin interpreter formats enums as `name(payload)`. This rewritten test locks the actual shared contract — payload crossing — using a payload read instead of the divergent stringification.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1

## Test Code
```
#template(T: type)
enum Wrap {
    w: T;
    none;
}
#template(T: type)
interface IBox {
    fun get(this) -> mut Wrap<T>;
}
class C {
    impl IBox<i32> {
        fun get(this) -> mut Wrap<i32> { return new Wrap<i32>.w(7); }
    }
}
initial {
    let b : IBox<i32> = cast<IBox<i32>>(new C());
    let w : Wrap<i32> = b.get();
    if (w is Wrap<i32>.w) {
        println(cast<string>(w.w));
    } else {
        println("wrong variant");
    }
}
```

## Compile
Args: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `7
`
ExpectedStderr: DISCARD
