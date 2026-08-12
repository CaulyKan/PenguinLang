# NewInstanceCompiledIntoConsumer
## Description
The lib ships `std.Vector<i64>` only; the consumer uses `std.Vector<Key>` where `Key` is a consumer-defined class. `std.Vector__Key` is NOT in the lib's shipped instances, so it is monomorphized INTO the consumer (C++-style template embedding — the lib's verbatim `export class Vector<T>` source carries the template body). Pass4-only.

## Apply To
* EmperorPenguin Pass4

## Test Code
```
fun __force_std_exports() {
    let _v = new std.Vector<i64>();
}
```
## Build 1
Kind: lib
Name: std.penguin-lib
Args: `EmperorPenguin/std/penguin/vector.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
class Key {
    id: i64 = 0;
    fun new(mut this, id: i64) { this.id = id; }
}
initial {
    let v = new std.Vector<Key>();
    v.push(new Key(1));
    v.push(new Key(2));
    v.push(new Key(3));
    println("size=" + cast<string>(v.size()));
    let k: Key = v.at(1).some;
    println("id1=" + cast<string>(k.id));
    let sum: mut i64 = 0;
    for (let x in v) { sum = sum + x.id; }
    println("sum=" + cast<string>(sum));
}
```
## Build 2
Args: `--lib ${WORKDIR}/std.penguin-lib`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `size=3
id1=2
sum=6
`
ExpectedStderr: DISCARD
