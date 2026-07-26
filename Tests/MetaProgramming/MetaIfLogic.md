# MetaIfLogic
## Description
Phase 5a: boolean operators in `#if` conditions. `#defined("X") || #defined("Y")` folds true (X is defined); `!#defined("Z")` folds true (Z is undefined). Both functions are included and summed.

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("X","1");
#if (#defined("X") || #defined("Y")) {
    fun flag() -> i64 { return 1; }
}
#if (!#defined("Z")) {
    fun flag2() -> i64 { return 2; }
}
initial {
    println(cast<string>(flag() + flag2()));
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
ExpectedStdout: EQUALS `3
`
ExpectedStderr: DISCARD
