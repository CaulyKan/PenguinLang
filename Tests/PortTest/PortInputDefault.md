# PortInputDefault
## Description
An unconnected input with an explicit default binds a constant source: `current()` reads the default value, and `wait` on it never delivers (the port is unconnected — the program ends quiescently).

## Apply To
* BabyPenguin
* EmperorPenguin Pass3

## Test Code
```
class Sensor {
    input x : i64 = 42;
    output y : i64;
    initial {
        let cur : __builtin.Option<i64> = this.x.current();
        if (cur.is_some()) {
            this.y.write(cur.some);
        }
    }
}

construct {
    let s : mut Sensor = new Sensor();
}

initial {
    let v : i64 = wait s.y;
    println(cast<string>(v));
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `42
`
ExpectedStderr: DISCARD
