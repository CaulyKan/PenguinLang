# IncrementalCascadeCrossFile
## Description
O1 sentinel (per-file A4 collector / incremental fixpoint): a body-only didChange on file A must make specializations of templates owned by CLEAN files appear through the CASCADE alone — round 0 collects `Holder<i32>` from the dirty body (template in cascade_meta2.penguin, never rescanned); round 1 scans the NEW `Holder$...` specialization's template body (its ast_source projection with the specialized class scope, `T`→`i32`) and requests `Box<i32>` — a use that exists in NO dirty file and NO scanned-clean-file body (the strict body-only cascade; the field/constructor resolve paths never see it). Green only if the incremental fixpoint's cascade over newly created specializations is complete. The template shape mirrors GenericNewInGenericMethod (the full-compile regression for the same body-only `new Wrap<T>`), split across files and driven through the fast lane. v1 prints "plain"; the edit swaps in the Holder usage; the run output proves both specializations were born and bound through the incremental path.

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
Args: `--disable-coroutine --incremental --edit=${WORKDIR}/source.penguin=Tests/IncrementalTest/cascade_v2.penguin Tests/IncrementalTest/cascade_meta1.penguin Tests/IncrementalTest/cascade_meta2.penguin`
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
