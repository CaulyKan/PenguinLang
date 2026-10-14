# Edge Detection — the free-mt parallel compute example

A Sobel edge detector, structured to use every bit of the multi-thread
scheduler's free-form policy (design doc §7.3 free-mt: "high-throughput
aggressive parallelism"; enabled by `--enable-multithread-scheduler`).
Two input modes:

- **Synthetic image** — `edge_detect [size]` (default 1024): a procedural
  test image (gradient background, filled circle, rectangle, diagonal band).
- **BMP file** — `edge_detect <in.bmp> [out.bmp]`: reads an uncompressed
  bottom-up 24-bit BMP (e.g. the bundled `Penguin.bmp`, 1024×1024), runs
  the same pipeline and writes an edges BMP (same geometry, edge pixels on
  black). `Penguin.bmp -> Penguin.edges.bmp` by default; pass an explicit
  output path as the second argument.

## Pipeline

```
[BMP gray extract |] generate -> blur_h -> blur_v -> sobel/threshold
```

Each stage fans out **32 band tasks** (`async fun`), joins them in band
order and stitches:

- **BMP gray extract** (BMP mode only) — grayscale (B+G+R)/3 halved into
  the pipeline's 0..127 range, straight out of the BMP bytes.
- **generate** — the synthetic test image so every stage has real edges.
- **blur_h / blur_v** — separable 1-3-5-3-1/16 Gaussian; the vertical pass
  reads its ±2-row halo straight from the stage input (global coordinates),
  so bands need no boundary hand-off.
- **sobel** — 3×3 Gx/Gy, magnitude threshold, binary edge map.

## How it parallelizes (and how to check)

The image lives as a **string** (one char per pixel, values 0..127 so a
pixel is exactly one char on both hosts). Strings are immutable — the Imm
tier of the sharing grid — so a band task's spawn arguments
(`kind: i64, img: string, w/h/y0/y1: i64`) are all shareable and **every
band task is Pass-10 STEALABLE**: workers genuinely distribute at spawn
time and idle workers steal unstarted bands. Verify with:

```sh
pass3 -Wconcurrency --enable-multithread-scheduler edge_detect.penguin   # 32x STEALABLE, 0 PINNED
```

All mutable state (the band's `StringBuilder`) is fresh-local; results
return through the spawn future as strings and the driver stitches — no
shared mutable structure anywhere, which is what keeps the parallel width
at the work's natural width rather than Pass 10's pinning floor.

Per-thread time breakdown (busy/idle/schedule/gc per worker):

```sh
EP_MULTITHREAD_SCHEDULER_PERF_ANALYZE=1 EMPEROR_THREADS=8 ./edge_detect Penguin.bmp
```

## Run

```sh
emperor_penguin edge_detect.penguin -o edge_detect        # needs --enable-multithread-scheduler semantics
EMPEROR_THREADS=8 ./edge_detect 1024          # synthetic image
EMPEROR_THREADS=8 ./edge_detect Penguin.bmp   # BMP in, Penguin.edges.bmp out
```

Output (deterministic for a given input — the checksum is identical across
EMPEROR_THREADS=1..8, which is itself a parallel-correctness check; the
BMP-mode count/checksum were verified against an independent reference
implementation of the pipeline):

```
edge_detect bmp 1024x1024
edges 12152 (1%)
checksum 6420563643560409083
```

## Performance (28-core i9, 1024×1024, interleaved ×5, medians)

| EMPEROR_THREADS | total | speedup |
|---|---|---|
| 1 | 461 ms | 1.00× |
| 2 | 258 ms | 1.79× |
| 4 | 155 ms | 2.97× |
| 8 | **95 ms** | **4.85×** |

Per-stage at N=8: gen 10 / blur_h 23 / blur_v 22 / sobel 34 ms — all four
stages scale; the residual gap to linear is the serial stitch between
stages plus spawn/join overhead (128 task lifecycles over the run).

Tuning notes: 32 bands give 4× oversubscription at N=8 (dynamic balance via
steal); the pixel-as-char encoding keeps every read a single codepoint
(`string_char_code_at`) and every write an interned-char append — building
the output through fresh per-pixel strings instead measured ~4.6× MORE CPU
under threads (allocator churn), which is why the char intern table exists
in the runtime. The BMP writer emits only bytes < 128 (header copied
verbatim from the input, pixels 0/100), so the output needs no binary
string construction.
