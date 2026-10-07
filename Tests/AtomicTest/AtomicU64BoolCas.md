# AtomicU64BoolCas
## Description
AtomicU64/AtomicBool (native-only additions beyond BabyPenguin's AtomicI64) and a compare-exchange retry loop: fetch_add returns the NEW value; bool swap returns the original; a CAS loop where failed exchanges retry (the loop-variable only advances on success) drives the counter to exactly 100 through 100 successful exchanges.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let u : mut __builtin.AtomicU64 = new __builtin.AtomicU64(2);
    let us : u64 = u.fetch_add(3);
    println("u64 " + cast<string>(us) + "," + cast<string>(u.load()));

    let b : mut __builtin.AtomicBool = new __builtin.AtomicBool(false);
    let ob : bool = b.swap(true);
    if (b.load()) {
        println("bool " + cast<string>(ob));
    }

    let n : mut i64 = 0;
    let c : mut __builtin.AtomicI64 = new __builtin.AtomicI64(0);
    while (n < 100) {
        let cur : i64 = c.load();
        if (c.compare_exchange(cur, cur + 1) == cur) {
            n = n + 1;
        }
    }
    println("cas-loop " + cast<string>(c.load()));
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
ExpectedStdout: EQUALS `u64 5,5
bool false
cas-loop 100
`
ExpectedStderr: DISCARD
