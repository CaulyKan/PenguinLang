# ArrayValueClassElements
## Description
Value-class elements copy whole-structure on set/at (slot stores its own copy, mutation through one read does not alias the slot).

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Point {
    impl ICopy<Point>;
    x: mut i64;
    y: mut i64;
    fun new(mut this, x: i64, y: i64) {
        this.x = x;
        this.y = y;
    }
}
initial {
    let pts : mut array<Point> = create_array(2, new Point(0, 0));
    pts.set(0, new Point(1, 2));
    let p : mut Point = pts.at(0).some;
    p.x = 99;
    // at() copied the value out; the slot keeps 1
    println("slotx=" + cast<string>(pts.at(0).some.x));
    println("localx=" + cast<string>(p.x));
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
ExpectedStdout: EQUALS `slotx=1
localx=99
`
ExpectedStderr: DISCARD
