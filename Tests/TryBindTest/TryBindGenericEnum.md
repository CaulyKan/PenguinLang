# TryBindGenericEnum
## Description
Try-bind with a generic enum payload: `if (let v := o.some)` where `o: __builtin.Option<i64>`. `v` gets the substituted payload type `i64`. Covers matched and non-matched paths.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    fun check(o: __builtin.Option<i64>) -> string {
        if (let v := o.some) {
            return "opt:" + cast<string>(v);
        }
        return "opt:none";
    }
    initial {
        println(check(new __builtin.Option<i64>.some(77)));
        println(check(new __builtin.Option<i64>.none()));
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
ExpectedStdout: EQUALS `opt:77
opt:none
`
ExpectedStderr: DISCARD
