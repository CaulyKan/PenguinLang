# ArrayNested
## Description
Nested native arrays `array<array<T>>`: outer array of arrays, inner arrays stay independent references.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let inner : mut array<i32> = create_array(3, 0);
    let grid : mut array<array<i32>> = create_array(2, inner);
    println("gridsize=" + cast<string>(grid.size()) + "," + cast<string>(grid.at(0).some.size()));
    println("grid00=" + cast<string>(grid.at(0).some.at(0).some));

    // rows share the same inner array reference (fill copies the reference)
    grid.at(0).some.set(1, 42);
    println("shared=" + cast<string>(grid.at(1).some.at(1).some));

    // rows are the same object: replacing a row only affects that slot
    let row : mut array<i32> = create_array(2, 9);
    grid.set(0, row);
    println("row0first=" + cast<string>(grid.at(0).some.at(0).some));
    println("row1second=" + cast<string>(grid.at(1).some.at(1).some));
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
ExpectedStdout: EQUALS `gridsize=2,3
grid00=0
shared=42
row0first=9
row1second=42
`
ExpectedStderr: DISCARD
