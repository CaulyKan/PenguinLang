# MetaDefineNestedInBranch
## Description
A code `#define` inside a taken `#if` branch records into the same file's view, so a NESTED `#if` inside that branch sees it (BP C# counterpart: NestedMetaInsideBranches).

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#define("OUTER","1");
#if (#defined("OUTER")) {
    #define("INNER","2");
    #if (#defined("INNER")) {
        fun deep() -> i64 { return 21; }
    }
}
initial {
    println(cast<string>(deep()));
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
ExpectedStdout: EQUALS `21
`
ExpectedStderr: DISCARD
