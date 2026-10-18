# ArrayNestedChainThenIterate
## Description
A chained read through a NESTED array (`grid.at(0).some.at(1).some`) followed by a for-in loop over a flat array. Locked the ArrayNestedChainThenIterate segfault (fixed 2026-10-17): `ensure_signature_instantiations` created signature-only enum specializations (e.g. `Option<i32>` reached only as `__array_at`'s return type) and stamped them pass-99 "processed", so their method bodies (is_some/is_none/value_or/get_unique_name) never bound — the emitter compiled each to GC-frame glue ending in `unreachable`, which at the machine level FALLS THROUGH into the next emitted function. Whether that corrupted execution crashed depended purely on the fall-through neighbor's shape (layout-sensitive: ASAN/canary/explicit-generic-arg builds masked it by reshuffling neighbors; BabyPenguin's tracing GC was never affected). In the crashing layout `Option<i32>.is_none` (called by the for-in desugaring's none-check) fell into `__array_at<i32>` executing with a 0x18-byte frame where its code expects 0x88 — the phantom invocation, the null receiver, and the corrupted return-address cascade. The fix removes the pass-99 stamp and runs the catch-up chain (passes 4..7 via catch_up_def_before_bodies when created in the pass-8 window) so the dynamic pass-8 loop binds method bodies. This test keeps both triggers together (nested-chain read + for-in) so a bodyless-method regression in ANY Option spec used by arrays fails loudly instead of depending on code-layout luck.

## Apply To
* BabyPenguin
* EmperorPenguin Pass1
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
initial {
    let a : mut array<i32> = create_array(3, 0);
    a.set(0, 7);
    let inner : mut array<i32> = create_array(3, 0);
    let grid : mut array<array<i32>> = create_array(2, inner);
    println("g=" + cast<string>(grid.at(0).some.at(1).some));
    let total : mut i64 = 0;
    for (let v2 in a) { total += v2; }
    println("sum=" + cast<string>(total));
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
ExpectedStdout: EQUALS `g=0
sum=7
`
ExpectedStderr: DISCARD
