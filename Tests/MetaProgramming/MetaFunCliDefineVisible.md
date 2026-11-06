# MetaFunCliDefineVisible
## Description
Batch 2.1/2.3 unified-store fix: CLI `--define` seeds ARE visible inside `#fun` bodies. Before the fix the MetaEngine kept its own option store that never received the CLI seed — both the unit-B `#defined` fold and the `#compiler().has_option/get_option` runtime callbacks read nothing (existing bug, probe=0/fold=0). Now the engine bridges to the host's file-scoped store keyed by the #fun's DEFINING file. All three probes must hit: score = 1 (has_option) + 2 (get_option value) + 4 (#defined fold) = 7. Requires native Pass2/Pass3 (JIT).

## Apply To
* EmperorPenguin Pass2
* EmperorPenguin Pass3

## Test Code
```
#fun opt_probe() -> i64 {
    let mut score = 0;
    if (compiler().has_option("CLIKEY")) { score = score + 1; }
    if (compiler().get_option("CLIKEY") == "clival") { score = score + 2; }
    if (#defined("CLIKEY")) { score = score + 4; }
    return score;
}
initial {
    println("probe=" + cast<string>(#opt_probe()));
}
```

## Compile
Args: `--define CLIKEY=clival`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `probe=7
`
ExpectedStderr: DISCARD
