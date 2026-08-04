# ListForImmutableContainerRead
## Description
Direct for-loop over an IMMUTABLE _utils.List (aliased) — reading an immutable container is
allowed because iter()/iter_mut() are non-mut-this (creating an iterator never mutates the
container). Green on all compilers.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let tmp : mut _utils.List<i64> = new _utils.List<i64>();
        tmp.push(1);
        tmp.push(2);
        let a : _utils.List<i64> = tmp;
        for (let x : i64 in a) {
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
