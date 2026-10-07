# AsyncSpawnKeywordName
## Description
RESOLVED (was the red sentinel AsyncSpawnAfterGlobalTouchFails): the "spawn synthesis corruption" was never about spawns or globals — the function was NAMED `pure`, a grammar keyword (functionSpecifier, PenguinLang.g4:297). ANTLR rejects `fun pure(...)` (an explicit keyword token beats the Identifier rule); EmperorPenguin's hand-written parser silently took the keyword token as the definition name, while every call site `pure(...)` lexed as the keyword and parsed into an EMPTY expression — surfacing much later as a confusing E_ASYNC_INVALID "'async' requires a function call" pointing nowhere near the cause. The parser now reports both positions loudly. This test asserts the compile FAILS (keyword as function name); the renamed positive variant is AsyncSpawnKeywordRenamed.

## Apply To
* BabyPenguin
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun pure(a: i64) -> i64 {
    return a + 1;
}

initial {
    let f = async pure(2);
    let r : i64 = wait f;
    println("pure " + cast<string>(r));
}
```

## Compile
Args: `--enable-coroutine`
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: ANY
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD
