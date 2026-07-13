# AtomicTest
## Description
AtomicI64 operations: load, store, compare_exchange, fetch_add, swap.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1 (SKIP if 'EmperorPenguin Pass2' PASS)
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
    initial {
        let a : mut AtomicI64 = new AtomicI64(1);
        println(cast<string>(a.load()));
        a.store(2);
        println(cast<string>(a.load()));
        let res1: i64 = a.compare_exchange(2, 3);
        println(cast<string>(res1));
        println(cast<string>(a.load()));
        let res2: i64 = a.compare_exchange(8888, 4);
        println(cast<string>(res2));
        println(cast<string>(a.load()));
        let res3 : i64 = a.fetch_add(1);
        println(cast<string>(res3));
        let res4 : i64 = a.swap(5);
        println(cast<string>(res4));
        println(cast<string>(a.load()));
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
ExpectedStdout: EQUALS `1
2
2
3
3
3
4
4
5
`
ExpectedStderr: DISCARD
