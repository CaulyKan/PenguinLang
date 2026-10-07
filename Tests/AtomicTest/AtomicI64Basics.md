# AtomicI64Basics
## Description
Native AtomicI64 semantics (multi-thread scheduler M1): swap returns the original; compare_exchange returns the original (success observable as original == expected); fetch_add returns the NEW value (BabyPenguin's Interlocked.Add semantics — the native implementation must match the reference VM value-for-value); store/load round-trip. Applies to BabyPenguin (the C# reference) and the native backends.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : mut __builtin.AtomicI64 = new __builtin.AtomicI64(10);
    let o1 : i64 = a.swap(20);
    let r1 : i64 = a.compare_exchange(20, 30);
    let r2 : i64 = a.compare_exchange(99, 40);
    let s1 : i64 = a.fetch_add(5);
    a.store(100);
    println("i64 " + cast<string>(o1) + "," + cast<string>(r1) + "," + cast<string>(r2) + "," + cast<string>(s1) + "," + cast<string>(a.load()));
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
ExpectedStdout: EQUALS `i64 10,20,30,35,100
`
ExpectedStderr: DISCARD
