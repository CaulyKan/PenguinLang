# TinyRiscvMultiCoreMt
## Description
The multi-core tinyriscv SoC under the MT scheduler (Pass3 + --enable-multithread-scheduler, EMPEROR_THREADS=4): same model as TinyRiscvMultiCore (4 independent RiscvCore instances under one esl.Clock; core 0 owns the UART, cores 1..3 run the compute firmware leaving 5*(800+400*id) at RAM word 0). Cores are memory-independent by construction, so the output is byte-identical to the classic scheduler and to EMPEROR_THREADS=1 — this pins the deferred-commit + steal + ESL component path (the combination that once had the N>=5 wakeup-protocol races) at the workload where they were found. The mt-compiled binary is also deterministic under EMPEROR_SHUFFLE_DELTAS (deferred-commit components only).

## Apply To
* EmperorPenguin Pass3

## Test Code
```
// The whole model is compiled via Compile.Args pointing at the project
// file: Examples/tinyriscv/tinyriscv.penguins (main + core/* + perips/* +
// sw/* + ../../EmperorPenguin/others/libpenguin-esl/*.penguin +
// std/penguin/vector.penguin, meta-sources = sw/Asm + sw/Firmware + esl Bit).
// This file contributes no definitions on top of the project.
```

## Compile
Args: `Examples/tinyriscv/tinyriscv.penguins --enable-coroutine --enable-multithread-scheduler`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `4`
Env: `EMPEROR_THREADS=4`
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `hello world
[tinyriscv] simulation end: 48614 cycles (972280 ns @ 50 MHz), 12 chars sent
[tinyriscv] core1 checksum 6000
[tinyriscv] core2 checksum 8000
[tinyriscv] core3 checksum 10000

`
ExpectedStderr: DISCARD
