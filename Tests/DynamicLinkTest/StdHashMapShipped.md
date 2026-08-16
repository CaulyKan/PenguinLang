# StdHashMapShipped
## Description
Build a `.penguin-lib` shipping `std.HashMap<i64,i64>` (from `hashmap.penguin` `export class HashMap<K,V>` + dependency `vector.penguin`; a seed instantiates `HashMap<i64,i64>`), then a consumer that puts/gets/removes/iterates. The HashMap instance + its transitive closure (Vector<u8>/Vector<i64>, _HashMapIterator, Option) are shipped and declared in the consumer. Pass4-only.

## Apply To
* EmperorPenguin Pass4

## Test Code
```
fun __force_std_exports() {
    let _m = new std.HashMap<i64, i64>();
}
```
## Build 1
Kind: lib
Name: std.penguin-lib
Args: `EmperorPenguin/std/penguin/vector.penguin EmperorPenguin/std/penguin/hashmap.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Test Code
```
initial {
    let mut m = new std.HashMap<i64, i64>();
    let i: mut i64 = 0;
    while (i < 5) { m.put(i, i * 100); i = i + 1; }
    println("size=" + cast<string>(m.size()));
    println("get3=" + cast<string>(m.get(3).some));
    m.remove(2);
    println("size2=" + cast<string>(m.size()));
    if (m.get(2).is_none()) { println("get2=none"); }
    let sum: mut i64 = 0;
    for (let p in m) { sum = sum + p.value; }
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
ExpectedStdout: EQUALS `size=5
get3=300
size2=4
get2=none
sum=800
`
ExpectedStderr: DISCARD
