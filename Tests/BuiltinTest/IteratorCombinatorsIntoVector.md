# IteratorCombinatorsIntoVector
## Description
The canonical combinator pipeline over a container — `v.iter().map(f).into<Vector<i64>>()` (vector.penguin is passed as an extra source: `_VectorIterator`'s `map` returns a `MapIterator<i64,i64>`, and `into<C>` materializes any iterator into a container with a default constructor + `push` — here `Vector<i64>`, spelled as an explicit type argument). The drained Vector is iterated back to prove the contents. Covers map-inference + into with a generic explicit type arg on both iterator classes.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let mut v0 = new std.Vector<i64>();
    v0.push(3);
    v0.push(1);
    v0.push(4);
    v0.push(1);
    v0.push(5);
    let out: std.Vector<i64> = v0.iter().map(fun (x: i64) -> i64 { return x + 10; }).into<std.Vector<i64>>();
    println("size=" + cast<string>(out.size()));
    let mut oi = out.iter();
    let e0: Option<i64> = oi.next();
    let e1: Option<i64> = oi.next();
    let e2: Option<i64> = oi.next();
    let e3: Option<i64> = oi.next();
    let e4: Option<i64> = oi.next();
    let e5: Option<i64> = oi.next();
    println(cast<string>(e0.some) + " " + cast<string>(e1.some) + " " + cast<string>(e2.some) + " " + cast<string>(e3.some) + " " + cast<string>(e4.some) + " " + cast<string>(e5.is_none()));
}
```

## Compile
Args: `EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `size=5
13 11 14 11 15 true
`
ExpectedStderr: DISCARD
