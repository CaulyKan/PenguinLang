# ArrayGcSurvival
## Description
GC survival: reference elements held in array slots stay alive across GC cycles (the marker walks array element slots). BabyPenguin runs with GC enabled (800K threshold); the loop churns enough garbage to trigger collections while the anchored array keeps its objects.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Box {
    impl IReferenceType;
    v: i64;
    fun new(mut this, v: i64) {
        this.v = v;
    }
}
initial {
    // anchor: one array whose elements must survive every GC cycle
    let keep : mut array<Box> = create_array(100, new Box(-1));
    let i : mut u64 = 0;
    while (i < 100) {
        keep.set(i, new Box(cast<i64>(i)));
        i = i + 1;
    }
    // churn garbage (fresh arrays + boxes every iteration)
    let j : mut u64 = 0;
    while (j < 20000) {
        let garbage : mut array<Box> = create_array(50, new Box(cast<i64>(j)));
        j = j + 1;
    }
    // verify all anchored elements survived with their values
    let total : mut i64 = 0;
    for (let b in keep) {
        total += b.v;
    }
    println("sum=" + cast<string>(total));
    println("len=" + cast<string>(keep.size()));
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
ExpectedStdout: EQUALS `sum=4950
len=100
`
ExpectedStderr: DISCARD
