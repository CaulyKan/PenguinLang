# TinyRiscvMultiCoreMt
## Description
The multi-core tinyriscv SoC under the MT scheduler (Pass3 + --enable-multithread-scheduler --enable-clock-model, EMPEROR_THREADS=4): same model as TinyRiscvMultiCore (4 independent RiscvCore instances under one esl.Clock; core 0 owns the UART, cores 1..3 run the compute firmware leaving 5*(800+400*id) at RAM word 0). Cores are memory-independent by construction, so the output is byte-identical to the classic scheduler and to EMPEROR_THREADS=1 — this pins the deferred-commit + steal + ESL component path (the combination that once had the N>=5 wakeup-protocol races) at the workload where they were found. The mt-compiled binary is also deterministic under EMPEROR_SHUFFLE_DELTAS (deferred-commit components only). Runtime is core-count sensitive: before the adaptive spin budget (scheduler.c), any oversubscription (scheduler participants > available cores — a CI runner sharing vCPUs with sibling test processes) collapsed throughput ~60-126x (N=2 on one core 300s+ unfinished vs 3s on two; N=4 on two cores 500s+ at ~1000 CPU-seconds) — the pre-futex spin window held the CPU the peer needed, degrading every park/wake round trip to OS scheduling latency while all stall/watchdog leashes stayed quiet (deltas kept converging, the clock kept advancing). The duration-based adaptive spin (fast catch < ~1M cycles recovers after 16 in a row; 3 wasted windows halve, floor 0 = park immediately, re-probe every 1024 parks; affinity prior starts decayed when participants > affine CPUs) restores every oversubscribed shape to the N=1 time (1.6-1.8s) with zero regression at full core (edge_detect N=1/8 unchanged to the millisecond). This test pins the fix at EMPEROR_THREADS=4 — the original CI failure shape; the generous per-test Timeout covers slow shared runners. when the scheduler's participant count exceeds the available cores (an oversubscribed CI runner, or any shared box) the per-delta park/wake barrier plus the boundary's full futures-waiter kick amplifies the total CPU work ~60x (measured on this binary: N=1 on one core 1s; N=2 on two cores 3s; N=2 on ONE core 300s+ unfinished; N=4 on two cores 500s+ unfinished at ~1000 CPU-seconds, still mid-output - deltas keep converging and the clock keeps advancing, so it is a throughput collapse under oversubscription, not a deadlock: the watchdog, idle-dump and stall leashes all stay quiet). A hosted CI runner cannot promise non-oversubscription, so the cross-machine gate runs the single-threaded form (byte-identical output by construction); the multi-thread race coverage at this workload (steal + deferred commit + ESL, the N>=5 wakeup-protocol hunting ground) belongs to local full-core hammers (EMPEROR_THREADS=2..8 with all cores free), and the light-weight mt tests with EMPEROR_THREADS=4 keep exercising the concurrent paths in the matrix regardless. The run keeps a generous per-test `Timeout:` as headroom for slow shared runners.

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
Args: `Examples/tinyriscv/tinyriscv.penguins --enable-coroutine --enable-multithread-scheduler --enable-clock-model`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `4`
Env: `EMPEROR_THREADS=4`
Timeout: 300
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `hello world
[tinyriscv] simulation end: 48614 cycles (972280 ns @ 50 MHz), 12 chars sent
[tinyriscv] core1 checksum 6000
[tinyriscv] core2 checksum 8000
[tinyriscv] core3 checksum 10000

`
ExpectedStderr: DISCARD
