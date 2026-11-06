# IncrementalSpecRecreate
## Description
O1 sentinel (scan-ledger attrition): a specialization's full lifecycle across three fast-lane rounds — born (v1 uses `Holder<i32>`, cascade creates `Holder$...` + `Box$...`), evicted (v2 drops the use; the O2 reconciliation's tree walk kills both and the scan ledger UNCOVERS their names), REBORN (v3 restores the use; the recreated specializations enter the append zone uncovered and must re-enter the incremental fixpoint's scan set — without the eviction hook's uncover, the covered-name residue would silently skip them and the re-request would rely on luck). The final program is v3's; its output proves the recreated chain works end to end.

## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let mut h = new app.Holder<i32>();
    println(h.stamp(41));
}
```
## Compile
Args: `--disable-coroutine --incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/cycle_v2.penguin --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/cycle_v3.penguin Tests/IncrementalTest/cascade_meta1.penguin Tests/IncrementalTest/cascade_meta2.penguin`
Env: ``
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
