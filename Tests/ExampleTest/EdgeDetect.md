# EdgeDetect
## Description
The free-mt parallel compute example (Examples/edgedetect/edge_detect.penguin), pinned at 256x256: Sobel edge detection over a synthetic image, four stages (generate / blur_h / blur_v / sobel) each fanned out over 32 Pass-10-STEALABLE band tasks (the image travels as a string — the Imm tier of the sharing grid — so every spawn argument is shareable). The checksum is identical across EMPEROR_THREADS (asserted here at the default N=1; the example's README documents N=1..8 agreement), making this both a correctness pin for the pipeline and a canary for the runtime pieces it exercises: the char intern table (single-char cast<string> results are GC-rooted statics), now_ms, and the per-band spawn/join machinery at 128 lifecycles per run.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
// The whole example is compiled via Compile.Args pointing at the source
// file: Examples/edgedetect/edge_detect.penguin (self-contained — core
// builtins only, no std modules). This file contributes no definitions.
```

## Compile
Args: `--enable-multithread-scheduler Examples/edgedetect/edge_detect.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `256`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `edge_detect 256x256
edges 3008 (4%)
checksum -5232188662568393029
`
ExpectedStderr: DISCARD
