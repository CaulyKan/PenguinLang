# ListTest
## Description
_utils.List basic operations: push, pop, size, at.

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
        println(cast<string>(a.size()));
        let res1 : Option<i64> = a.at(0);
        println(cast<string>(res1.some));
        let res2 : Option<i64> = a.at(2);
        println(cast<string>(res2.some));
        a.pop();
        println(cast<string>(a.size()));
        let res3 : Option<i64> = a.at(1);
        println(cast<string>(res3.some));
        let res4 : Option<i64> = a.at(2);
        println(cast<string>(res4.is_none()));
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
ExpectedStdout: EQUALS `3
1
3
2
2
true
`
ExpectedStderr: DISCARD
