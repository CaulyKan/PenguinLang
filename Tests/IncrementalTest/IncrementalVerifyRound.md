# IncrementalVerifyRound
## Description
O1 sentinel (the safety net): with EMPEROR_INCREMENTAL_VERIFY_INTERVAL=1 every fast-lane round runs the incremental fixpoint FIRST and then the full-collection verification fixpoint (heal + per-file request-cache compare + rebuild). Two body-only edits create a cross-file cascade (round: Holder$... then Box$...), then edit it again — every round is both incremental AND verified, so the byte-exact output proves (a) the verification pass does not double-create or otherwise disturb the tree, (b) the cache rebuild keeps subsequent incremental rounds correct. The default interval (20) case is covered by the other IncrementalTest sentinels (single edits never verify).

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    println("plain");
}
```
## Compile
Args: `--disable-coroutine --incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/verify_v2.penguin --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/verify_v3.penguin Tests/IncrementalTest/cascade_meta1.penguin Tests/IncrementalTest/cascade_meta2.penguin`
Env: `EMPEROR_INCREMENTAL_VERIFY_INTERVAL=1`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedStdout: EQUALS `box!
`
ExpectedExitCode: 0
