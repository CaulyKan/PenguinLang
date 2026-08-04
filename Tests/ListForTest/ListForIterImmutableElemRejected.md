# ListForIterImmutableElemRejected
## Description
For-loop over mut List<Foo>.iter() (elements immutable) calling a 'mut this' method on the
element -> compile must FAIL. mut List<Foo> means elements are immutable by default.
Green on all compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Foo { val: mut i64 = 0; fun new(mut this, v: i64) { this.val = v; } fun setVal(mut this, v: i64) { this.val = v; } fun getVal(this) -> i64 { return this.val; } }
    initial {
        let a : mut _utils.List<Foo> = new _utils.List<Foo>();
        a.push(new Foo(1));
        for (let x : Foo in a.iter()) {
            x.setVal(10);
        }
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_TYPE_MISMATCH`
