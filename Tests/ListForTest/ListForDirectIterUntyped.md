# ListForDirectIterUntyped
## Description
Direct for-loop with an UNTYPED loop variable (let x) — the type is inferred from the iterator's
element type. Green on all compilers.

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
        for (let x in a) {
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
