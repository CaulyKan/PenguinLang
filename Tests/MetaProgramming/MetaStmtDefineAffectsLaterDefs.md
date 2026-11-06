# MetaStmtDefineAffectsLaterDefs
## Description
Statement-level `#define` is processed by the COMPILE-TIME ordered splice walk (batch 2.1 merged the statement walk into the def walk on EP, matching BP's single ordered pass): the enclosing function never needs to run — from its source line on, later definitions' AND statements' `#if` conditions in the same file observe the key.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun seed_one() -> i64 {
    #define("LATE","1");
    return 0;
}
#if (#defined("LATE")) {
    fun after() -> i64 { return 11; }
}
fun seed_two() -> i64 {
    #define("LATE2","1");
    return 0;
}
fun late_if() -> i64 {
    #if (#defined("LATE2")) {
        return 22;
    }
    return 33;
}
initial {
    println(cast<string>(after()));
    println(cast<string>(late_if()));
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
ExpectedStdout: EQUALS `11
22
`
ExpectedStderr: DISCARD
