# ListForImmutableContainerImmutableElemRejected
## Description
Direct for-loop over an IMMUTABLE List<Foo> calling a 'mut this' method -> must fail (container
AND elements immutable). Green on all compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Foo { val: mut i64 = 0; fun new(mut this, v: i64) { this.val = v; } fun setVal(mut this, v: i64) { this.val = v; } fun getVal(this) -> i64 { return this.val; } }
    initial {
        let tmp : mut _utils.List<Foo> = new _utils.List<Foo>();
        tmp.push(new Foo(1));
        let a : _utils.List<Foo> = tmp;
        for (let x : Foo in a) {
            x.setVal(10);
        }
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_`
