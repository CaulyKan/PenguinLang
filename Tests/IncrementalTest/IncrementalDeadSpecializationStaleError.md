# IncrementalDeadSpecializationStaleError
## Description
O2c sentinel (reconciliation split): a specialized def that carried errors in the PREVIOUS round and is no longer requested after the edit must not leave its old errors behind as PHANTOM diagnostics. Repro shape (2-file project, edit touches only the caller's body => fast lane): `deadspec_meta.penguin` owns the template; the v1 caller requests `app.badm<i64>(1)` so the `badm$...i64` specialization is created and reports its own E_RETURN_TYPE_MISMATCH; the v2 body drops the call, the reconciliation evicts the dead specialization — but the fast lane's error replay used to replay the (clean) template file's snapshot error list VERBATIM, resurrecting the evicted specialization's error ("... in badm$2F..." in stderr) even though a full compile of the v2 texts has no such error. Root cause: replayed snapshot errors had no owner attribution. Fix (O2c): errors captured during per-def processing carry `owner_def` (the pass-8 body loop and pass-9 validate loop set `model.current_binding_def`; report_* captures it into `SemanticError.owner_def`), and `IncrementalSession.replay_errors` drops replayed errors whose owner was evicted by this round's reconciliation (`last_evicted_dead`). Expected post-fix stderr: exactly ONE error — the template's own (still real) return-type mismatch. Note the deliberate tolerance: the FULL compile of these texts also carries a location-less pass-8 `Type 'T' has no member` error which the fast lane drops by the known batch-4.4 tradeoff (location-less pass>=3 entries are not replayed); this sentinel locks the fast lane's stable improved form — no dead-instance error, exit 1 for the genuinely remaining template error. Green on BabyPenguin/Pass1/2/3 once the owner filter is in; red (two `badm` errors, the second naming the mangled specialization) before it.
## Apply To
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
fun work() -> i64 {
    return app.badm<i64>(1);
}

initial {
    println(cast<string>(work()));
}
```
## Compile
Args: `--disable-coroutine --incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/deadspec_v2.penguin Tests/IncrementalTest/deadspec_meta.penguin`
Env: ``
ExpectedExitCode: 1
ExpectedStdout: DISCARD
ExpectedStderr: MATCH `[EmperorPenguin] Compilation failed with 1 errors:
  error[E_RETURN_TYPE_MISMATCH]: Return type mismatch: expected i64 but got void in badm (at Tests/IncrementalTest/deadspec_meta.penguin:4,5)
`
