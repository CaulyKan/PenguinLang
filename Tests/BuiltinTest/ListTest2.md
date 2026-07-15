# ListTest2
## Description
List passed to a function that adds elements.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun test(a: mut _utils.List<string>) {
        for (let i : i64 in range(0, 2)) {
            let s = cast<string>(i);
            a.push(s);
        }
    }
    initial {
        let a : mut _utils.List<string> = new _utils.List<string>();
        test(a);
        println(cast<string>(a.size()));
        let i: mut i64 = 0;
        while (i < cast<i64>(a.size())) {
            let op = a.at(cast<u64>(i));
            if (op.is_some()) {
                print(op.some);
            }
            i = i + 1;
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
ExpectedStdout: EQUALS `2
01`
ExpectedStderr: DISCARD
