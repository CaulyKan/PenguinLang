# ArrayBasic
## Description
Native array `array<T>`: create_array with fill, set/at/size magic methods, out-of-bounds at returns none and set is a no-op, create_array_with, for-in iteration.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : mut array<i32> = create_array(5, 0);
    a.set(0, 10);
    a.set(1, 20);
    a.set(2, 30);
    println("len=" + cast<string>(a.size()));
    println("a0=" + cast<string>(a.at(0).some));
    println("a1=" + cast<string>(a.at(1).some));
    println("a2=" + cast<string>(a.at(2).some));
    println("fill=" + cast<string>(a.at(3).some) + "," + cast<string>(a.at(4).some));

    if (a.at(100) is Option<i32>.none) {
        println("oob=none");
    }
    a.set(100, 999);
    println("len2=" + cast<string>(a.size()));

    let b : mut array<i64> = create_array_with(4, fun (i: u64) -> i64 { return cast<i64>(i) * cast<i64>(i); });
    println("b0=" + cast<string>(b.at(0).some));
    println("b3=" + cast<string>(b.at(3).some));

    let total : mut i64 = 0;
    for (let v in b) {
        total += v;
    }
    println("sum=" + cast<string>(total));
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
ExpectedStdout: EQUALS `len=5
a0=10
a1=20
a2=30
fill=0,0
oob=none
len2=5
b0=0
b3=9
sum=14
`
ExpectedStderr: DISCARD
