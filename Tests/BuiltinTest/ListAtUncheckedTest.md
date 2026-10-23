# ListAtUncheckedTest
## Description
`_utils.List.at_unchecked` — bare element access added as a hot-path alternative
to `at()` (no bounds branch, no Option boxing; used by StrMap probes and the
bound-tree def-index lookups in the compiler). This locks in its behavior: it
returns exactly the element `at()` would yield for an in-range index, for both
reference and value element types.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    fun pick(l: _utils.List<string>, i: u64) -> string {
        return l.at_unchecked(i);
    }
    initial {
        let a : mut _utils.List<string> = new _utils.List<string>();
        a.push("alpha");
        a.push("beta");
        a.push("gamma");
        println(pick(a, 0));
        println(a.at_unchecked(cast<u64>(2)));
        let nums : mut _utils.List<i64> = new _utils.List<i64>();
        let i: mut i64 = 0;
        while (i < 4) {
            nums.push(i * i);
            i = i + 1;
        }
        let sum: mut i64 = 0;
        let j: mut i64 = 0;
        while (j < cast<i64>(nums.size())) {
            sum = sum + nums.at_unchecked(cast<u64>(j));
            j = j + 1;
        }
        println(cast<string>(sum));
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
ExpectedStdout: EQUALS `alpha
gamma
14
`
ExpectedStderr: DISCARD
