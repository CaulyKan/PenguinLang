# EdgeDetectBmp
## Description
BMP mode of the free-mt edge-detection example (`Examples/edgedetect/edge_detect.penguin`): reads an uncompressed bottom-up 24-bit BMP (`Examples/edgedetect/Penguin.bmp`, 1024x1024), extracts the grayscale plane ((B+G+R)/3 halved into the pipeline's 0..127 range), runs the 32-band blur/blur/sobel pipeline and writes a 24-bit edges BMP (header copied verbatim from the input; edge pixels 100 on black — every emitted byte < 128). The printed edge count and FNV checksum were verified against an independent Python reference implementation of the same pipeline (clamped-border 1-3-5-3-1/16 blur ×2, sobel |gx|+|gy|>48, values 0/100, signed-i64 FNV-1a) — byte-exact agreement. Uses the renamed flag surface: `--enable-multithread-scheduler` alone = the free-form policy (scheduler_free.penguin).


## Apply To
* EmperorPenguin Pass3

## Test Code
```
// The whole example is compiled via Compile.Args pointing at the source
// file: Examples/edgedetect/edge_detect.penguin (self-contained). This
// file contributes no definitions.
```

## Compile
Args: `--enable-multithread-scheduler Examples/edgedetect/edge_detect.penguin`
Env: ``
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: `${PENGUIN_ROOT}/Examples/edgedetect/Penguin.bmp out.bmp`
Env: ``
Stdin: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `edge_detect bmp 1024x1024
edges 12152 (1%)
checksum 6420563643560409083
`
ExpectedStderr: DISCARD
