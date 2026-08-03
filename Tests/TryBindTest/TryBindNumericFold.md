# TryBindNumericFold
## Description
Try-bind numeric cast checks: `let a : u16 := b`. Lossless widening (`can_widen_primitive`) is a compile-time decision — `u8 → u16` folds TRUE, `i32 → u16` folds FALSE (dead branch). Same-type casts fold TRUE.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __nf {
    fun num_fold(small: u8, big: i32) -> string {
        let r1 = (let a : u16 := small);
        let r2 = (let a : u16 := big);
        return cast<string>(r1) + "," + cast<string>(r2);
    }
    initial {
        println(num_fold(cast<u8>(5), 100000));
    }
}
```

## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `true,false
`
ExpectedStderr: DISCARD
