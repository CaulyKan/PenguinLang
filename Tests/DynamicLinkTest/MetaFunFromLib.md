# MetaFunFromLib
## Description
The lib ships `std.HashMap<i64,i64>`; its verbatim source embeds the `#fun require_ihash` compile-time key check. The consumer uses `std.HashMap<string, i64>` — a NEW instance NOT shipped by the lib — so it is monomorphized into the consumer, whose compile-time call `#require_ihash(#typeof(string))` JIT-runs the LIB's `#fun require_ihash` on the consumer side. Verifies meta #fun bodies travel with the lib source and JIT correctly in the consumer. Pass4-only.

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
    let mut m = new std.HashMap<string, i64>();
    m.put("apple", 1);
    m.put("banana", 2);
    println("size=" + cast<string>(m.size()));
    println("banana=" + cast<string>(m.get("banana").some));
    if (m.get("kiwi").is_none()) { println("kiwi=none"); }
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
ExpectedStdout: EQUALS `size=2
banana=2
kiwi=none
`
ExpectedStderr: DISCARD
