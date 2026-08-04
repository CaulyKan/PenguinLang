# ListForIterUntyped
## Description
For-loop over .iter() with an UNTYPED loop variable — type inferred from the iterator element.
Green on all compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : mut _utils.List<i64> = new _utils.List<i64>();
        a.push(1);
        a.push(2);
        for (let x in a.iter()) {
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
ExpectedStdout: EQUALS `12`
ExpectedStderr: DISCARD
