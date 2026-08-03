# TryBindWhileEnum
## Description
Try-bind in a `while` condition: `while (let x := cur.some)` re-binds `x` to the payload each iteration as the enum advances. Expected output `123`.

## Apply To
* BabyPenguin
* BabyPenguin CS
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
namespace __c1 {
    enum OptVal { some: u8; none; }
    fun next_opt(v: OptVal) -> mut OptVal {
        if (v is OptVal.some) { return new OptVal.some(v.some + 1); }
        return new OptVal.none();
    }
    fun walk() -> string {
        let cur : mut OptVal = new OptVal.some(1);
        let mut sb = new StringBuilder();
        while (let x := cur.some) {
            sb.append(cast<string>(x));
            if (x >= 3) { break; }
            cur = next_opt(cur);
        }
        return sb.to_string();
    }
    initial {
        println(walk());
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
ExpectedStdout: EQUALS `123
`
ExpectedStderr: DISCARD
