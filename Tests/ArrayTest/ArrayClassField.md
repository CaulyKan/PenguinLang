# ArrayClassField
## Description
Arrays as class fields: the field holds the array reference; element writes through the field are visible through other references to the same object.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
class Bag {
    items: mut array<i64>;
    fun new(mut this, n: u64, init: i64) {
        this.items = create_array(n, init);
    }
    fun bump(mut this, i: u64, d: i64) {
        let v : i64 = this.items.at(i).some;
        this.items.set(i, v + d);
    }
}
initial {
    let b : mut Bag = new Bag(3, 10);
    let alias : mut Bag = b;
    alias.bump(1, 5);
    println("b1=" + cast<string>(b.items.at(1).some));
    println("size=" + cast<string>(b.items.size()));
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
ExpectedStdout: EQUALS `b1=15
size=3
`
ExpectedStderr: DISCARD
