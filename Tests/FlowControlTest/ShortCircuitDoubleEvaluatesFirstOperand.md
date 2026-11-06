# ShortCircuitDoubleEvaluatesFirstOperand
## Description
4.4 handoff bug ("cross dyn-lib bool return value fed straight into `||` short-circuit is corrupted" — the main debugging sink of the LSP single-actor batch): IRGenerator.lower_binary lowered operand 0 of a binary expression UP FRONT ("derive type if void") and then dispatched the whole chain to lower_short_circuit / the `is` path, which lower every operand AGAIN — the first operand of `a || b` / `a && b` executed TWICE with the first result silently discarded. For the LSP's `changed = IncrementalSession.update(file, text) || changed` (a cross-.so call that returns true only while the text CHANGES) the discarded first call ingested the edit, and the observed second call returned false — "update reports false when it must be true", looking like an ABI/register corruption (observation code shifting register allocation shifted the manifestation). Fix (batch 5 C2): scan the operators BEFORE lowering anything; the short-circuit and `is` paths lower each operand exactly once, and the eager first-operand lowering survives only on the plain chain path. BabyPenguin (the reference VM) always evaluated operands once, so it locks the correct semantics. Asserts: an operand with a side effect (a hit counter) executes exactly once per appearance, an once-true-then-false callee feeds `||` correctly (the exact LSP shape: the SECOND call returns false — the pre-fix compiler ran it twice and read the second), and `&&`/`||` short-circuit semantics stay intact.
## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3
## Test Code
```
class Probe {
    hits: mut i64 = 0;
    done: mut bool = false;
    fun new(mut this) {}
    fun first_false(mut this) -> bool {
        this.hits += 1;
        return false;
    }
    fun once_true(mut this) -> bool {
        this.hits += 1;
        return true;
    }
    // The LSP shape: true only on the FIRST call (an update() that already
    // ingested the text returns false). Feeding this straight into `||` must
    // observe the FIRST call's true — the pre-fix compiler ran it twice and
    // read the second (false).
    fun update_once(mut this) -> bool {
        this.hits += 1;
        if (!this.done) {
            this.done = true;
            return true;
        }
        return false;
    }
}

initial {
    let p: mut Probe = new Probe();
    let a: mut bool = false;
    a = p.first_false() && true;
    let b: mut bool = true;
    b = p.once_true() || false;
    let c: mut bool = false;
    c = p.update_once() || c;
    println("hits=" + cast<string>(p.hits) + " a=" + cast<string>(a) + " b=" + cast<string>(b) + " c=" + cast<string>(c));
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
ExpectedExitCode: 0
ExpectedStdout: EQUALS `hits=3 a=false b=true c=true
`
ExpectedStderr: DISCARD
