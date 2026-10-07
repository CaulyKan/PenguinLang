# TinyRiscvMultiCore
## Description
The multi-core tinyriscv SoC: main's construct instantiates N independent RiscvCore instances (argv[0], here 3) under one esl.Clock — each core carries its own pipeline, bus, ROM and RAM; core 0 additionally owns the UART and runs the hello-world firmware, cores 1..N-1 run the compute firmware (a counted loop whose bound is elaboration-preloaded at their RAM word 2: 800 + 400*id iterations of sum += 5, leaving 5*bound at word 0 and a done flag at word 1). Cores are memory-independent by construction, so core 0's timing is IDENTICAL to the single-core model — the cycle count pins at 48614 — and every per-core result is deterministic regardless of scheduler or thread placement. SimEnd fires when core 0's x26 is set AND every compute core's done flag is up, prints the per-core checksums, then drains the UART and exits explicitly.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
// The whole model is compiled via Compile.Args pointing at the project
// file: Examples/tinyriscv/tinyriscv.penguins (main + core/* + perips/* +
// sw/* + ../../EmperorPenguin/others/libpenguin-esl/*.penguin +
// std/penguin/vector.penguin, flags --enable-coroutine, meta-sources =
// sw/Asm + sw/Firmware + esl Bit). This file contributes no definitions on
// top of the project.
```

## Compile
Args: `Examples/tinyriscv/tinyriscv.penguins --enable-coroutine`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `3`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `hello world
[tinyriscv] simulation end: 48614 cycles (972280 ns @ 50 MHz), 12 chars sent
[tinyriscv] core1 checksum 6000
[tinyriscv] core2 checksum 8000

`
ExpectedStderr: DISCARD
