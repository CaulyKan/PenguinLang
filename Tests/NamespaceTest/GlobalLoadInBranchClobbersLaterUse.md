# GlobalLoadInBranchClobbersLaterUse
## Description
BabyPenguin IR generation bug (found during batch 5 MetaHost work, FIXED): a NAMESPACE-LEVEL GLOBAL accessed inside a branch body (if/while) and then AGAIN after the block, within the same function, broke the later access — IRGenerator's ResolveReg memoized the global's GLOBAL_LOAD register at its first resolution, so when the first use sat inside a conditional branch the later use after the merge point reused the register without re-loading; on the call where the branch was skipped the register held a void-typed uninitialized value ("Cannot read field 'push' from NotInitializedRuntimeValue (type=!mut void)"; for an i64 global, "Cannot evaluate binary op on non-basic values"). Both the VM and the CS backend consume the same IR, so both were hit. Fix: emit a fresh GLOBAL_LOAD at every use (loads alias the global storage object; writes sync back via GLOBAL_STORE). Correct behavior asserted: both calls land their pushes (branch taken then not taken) → size 3, exit 0.
## Apply To
* BabyPenguin
* BabyPenguin CS
## Test Code
```
namespace emperor {
let g_list: mut _utils.List<i64> = new _utils.List<i64>();

fun bump(fp: string) {
    if (string_length(fp) >= 6) {
        g_list.push(2);
    }
    g_list.push(1);
}
}

initial {
    emperor.bump("abcdef");
    emperor.bump("ab");
    println(cast<string>(emperor.g_list.size()));
}
```
## Compile
Args: ``
Env: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `3
`
ExpectedStderr: DISCARD
