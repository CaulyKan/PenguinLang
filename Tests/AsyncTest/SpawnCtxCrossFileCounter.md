# SpawnCtxCrossFileCounter
## Description
Cross-file spawn-site synthetic-name collision guard (batch 1.3 regression). Two source files share the SAME explicit namespace `spawnx` (namespaces merge across files), and each spawns a call with a DIFFERENT capture set: spawnx_a.penguin's `async g3(1,2,3)` captures 3 args (ctx class has fields a0/a1/a2 + fut), spawnx_b.penguin's `async h0()` captures none. The synthesized `__SpawnCtx_<n>`/`__SpawnFut_<n>` classes are bound into the spawn site's ENCLOSING namespace scope — the shared `spawnx` namespace, NOT a per-file `_ns_<file>` scope — so per-file counters mint `__SpawnCtx_1` in BOTH files and the two identically named ctx/future classes collide in one scope: the loser silently replaces/gets replaced and its future completes to the wrong value (the 1.3 bug: tinyriscv failed with `E_RESOLVE_SYMBOL: Type '__SpawnCtx_1' has no member 'a0'`, MultiInputConnect segfaulted, PipeChannel lost a wire; in this repro r2 printed 0 instead of 7). Batch 1.3 reverted to GLOBAL counters; batch 3.4 (invalidation class 5) re-localized them FILE-QUALIFIED: the counters are per-file AND the names embed the owning file's namespace tag (__SpawnCtx__ns_<stem>_<hash>_<n>), so same-bucket numbering can never collide across files regardless of the registration scope -- this test locks that property. The two fixture files ride along via Compile.Args — the collision needs both files in ONE compilation unit, not separate libs. BabyPenguin cannot apply (its backend ignores Compile.Args, so the fixtures never join its unit).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    spawnx.fa();
    spawnx.fb();
}
construct { }
```

## Compile
Args: `Tests/fixtures/spawnx_a.penguin Tests/fixtures/spawnx_b.penguin --enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `r1=123
r2=7
`
ExpectedStderr: DISCARD
