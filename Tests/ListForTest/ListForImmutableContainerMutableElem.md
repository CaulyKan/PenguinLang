# ListForImmutableContainerMutableElem
## Description
Direct for-loop over an IMMUTABLE List<mut Foo> (aliased container): container ops forbidden but
element mutation allowed (mut in the type arg). The loop variable is a COPY of the value-class
element (value-copy semantics), so setVal mutates the copy — prints 12.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Foo { val: mut i64 = 0; fun new(mut this, v: i64) { this.val = v; } fun setVal(mut this, v: i64) { this.val = v; } fun getVal(this) -> i64 { return this.val; } }
    initial {
        let tmp : mut _utils.List<mut Foo> = new _utils.List<mut Foo>();
        tmp.push(new Foo(1));
        tmp.push(new Foo(2));
        let a : _utils.List<mut Foo> = tmp;
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
