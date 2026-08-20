# ListForDirectMutableElem
## Description
Direct for-loop over mut List<mut Foo>, calling a 'mut this' method on each element. The mutable
loop variable (let x : mut Foo) selects iter_mut(); the loop variable is a COPY of the value-class
element (value-copy semantics — `List<mut T> ≡ List<T>` for value elements), so setVal mutates the
copy and the list keeps the original values — prints 12. Write back with set() when the mutation
must stick.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Foo { val: mut i64 = 0; fun new(mut this, v: i64) { this.val = v; } fun setVal(mut this, v: i64) { this.val = v; } fun getVal(this) -> i64 { return this.val; } }
    initial {
        let a : mut _utils.List<mut Foo> = new _utils.List<mut Foo>();
        a.push(new Foo(1));
        a.push(new Foo(2));
        for (let x : mut Foo in a) {
            x.setVal(x.getVal() + 10);
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
