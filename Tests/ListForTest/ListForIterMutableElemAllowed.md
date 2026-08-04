# ListForIterMutableElemAllowed
## Description
For-loop over mut List<mut Foo>.iter() (elements mutable) calling a 'mut this' method -> must
compile and mutate. List<mut Foo> means elements are mutable. Green on all compilers.

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
        for (let x : mut Foo in a.iter()) {
            x.setVal(x.getVal() + 10);
        }
        for (let x : mut Foo in a.iter()) {
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
