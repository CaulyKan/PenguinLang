# TinyRiscvHelloWorld
## Description
The tinyriscv ESL model end to end (Examples/tinyriscv): a real RV32I firmware (assembled AT COMPILE TIME by the #fun meta path from Examples/tinyriscv/sw/Firmware.penguin) executes on the cycle-accurate 3-stage pipeline model, polls the UART model's STATUS register (each poll costs real pipeline cycles), sends "hello world\n" at 10 bits × 441 cycles per char, and sets x26=1 (testbench end convention). The final report line pins the exact cycle count — 48614 cycles × 20 ns = 972280 ns at the tinyriscv testbench's 50 MHz. NOTE the trailing blank line: the firmware sets x26=1 WITHOUT waiting for the last character's stop bit (faithful to the testbench convention), so the final '\n' character prints one line AFTER the report — deterministic ordering. Requires native Pass3 with the coroutine flag and the project's meta-sources unit-B list (the whole model project is compiled via its .penguins file).

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
Args: ``
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `hello world
[tinyriscv] simulation end: 48614 cycles (972280 ns @ 50 MHz), 12 chars sent

`
ExpectedStderr: DISCARD
