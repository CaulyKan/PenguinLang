# IteratorCombinators
## Description
Iterator combinators on `range()` — map/filter/reduce/all/any as generic methods on the concrete iterator classes (map/reduce type args INFERRED from the lambda/init argument types, no explicit `<...>` needed at the call; filter/all/any are non-generic). A map->filter->reduce chain, direct next() pulls off a mapped iterator, and all/any predicates over a mapped stream.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let mut d = range(0, 6).map(fun (x: i64) -> i64 { return x * 2; });
    let a: Option<i64> = d.next();
    let b: Option<i64> = d.next();
    println("pull=" + cast<string>(a.some) + "," + cast<string>(b.some));

    let total: i64 = range(0, 6).map(fun (x: i64) -> i64 { return x * 2; }).filter(fun (x: i64) -> bool { return x < 6; }).reduce(0, fun (acc: i64, x: i64) -> i64 { return acc + x; });
    println("total=" + cast<string>(total));

    let allsmall: bool = range(0, 6).map(fun (x: i64) -> i64 { return x * 2; }).all(fun (x: i64) -> bool { return x < 10; });
    println("all=" + cast<string>(allsmall));
    let anybig: bool = range(0, 6).map(fun (x: i64) -> i64 { return x * 2; }).any(fun (x: i64) -> bool { return x > 8; });
    println("any=" + cast<string>(anybig));
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
ExpectedStdout: EQUALS `pull=0,2
total=6
all=false
any=true
`
ExpectedStderr: DISCARD
