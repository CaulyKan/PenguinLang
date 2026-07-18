# ListForEachTest
## Description
For-each loop over List.iter().

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : mut _utils.List<i64> = new _utils.List<i64>();
        a.push(1);
        a.push(2);
        a.push(3);
        for (let x : i64 in a.iter()) {
            print(cast<string>(x));
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
ExpectedStdout: EQUALS `123`
ExpectedStderr: DISCARD
