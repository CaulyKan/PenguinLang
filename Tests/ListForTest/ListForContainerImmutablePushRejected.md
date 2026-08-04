# ListForContainerImmutablePushRejected
## Description
Pushing to an IMMUTABLE _utils.List must fail — List<Foo> cannot add elements (container ops
require the outer mut). Green on all compilers.

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
        let a : _utils.List<i64> = tmp;
        a.push(2);
    }
```

## Compile
Args: `${PENGUIN_ROOT}/EmperorPenguin/src/utils.penguin`
Env: ``
ExpectedExitCode: NONZERO
ExpectedStdout: DISCARD
ExpectedStderr: CONTAINS `E_TYPE_MISMATCH`
