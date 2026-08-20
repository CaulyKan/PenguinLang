# ListValueElemSetWriteback
## Description
Value-copy semantics on container elements: `at()`/loop-variable access of a
value-class element is a COPY (`List<mut T> ≡ List<T>` for value elements), so
mutating the copy does not touch the list. The write-back idiom is explicit:
mutate the copy and `set()` it back into the slot. The mutation then sticks —
prints 1112. Complements ListForDirectMutableElem (copy semantics without
write-back).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Foo { val: mut i64 = 0; fun new(mut this, v: i64) { this.val = v; } fun setVal(mut this, v: i64) { this.val = v; } fun getVal(this) -> i64 { return this.val; } }
initial {
    let a : mut _utils.List<mut Foo> = new _utils.List<mut Foo>();
    a.push(new Foo(1));
    a.push(new Foo(2));
    let i : mut i64 = 0;
    while (i < a.size()) {
        let x : mut Foo = a.at(cast<u64>(i)).some;
        x.setVal(x.getVal() + 10);
        a.set(cast<u64>(i), x);
        i = i + 1;
    }
    for (let x : mut Foo in a) {
        print(cast<string>(x.getVal()));
    }
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
ExpectedStdout: EQUALS `1112`
ExpectedStderr: DISCARD
