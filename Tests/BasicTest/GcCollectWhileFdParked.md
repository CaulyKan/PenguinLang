# GcCollectWhileFdParked
## Description
GC collections running while another coroutine sits fd-parked must scan that coroutine's stack region safely. This is the stress sentinel for the scan path behind a real crash: the scheduler records a parked coroutine's stack extent as `sp_park = &marker` of a `char` local, which is typically NOT 8-byte aligned (observed ≡7 mod 8); `_emperor_gc_scan_set_live` stored it verbatim and `_emperor_gc_collect` stepped `(void**)p` from that unaligned low end while `p < end` — the final read could begin inside the region yet extend 7 bytes past `base+bytes`, across the mmap boundary of the 32MB stack block (SIGSEGV, e.g. inside `json_escape` allocating during a collect while another coroutine sat parked). Whether a given binary actually crashes depends on the parked frame chain's total size landing on an unaligned offset (the 8-coroutine LSP server did — its first publishDiagnostics crashed inside `json_escape`'s allocation; simple programs happen to align). The deterministic byte-exact repro of that crash is LspTest/SessionLifecycle.md; `_emperor_gc_scan_set_live` now rounds live_lo DOWN to pointer alignment (collect-time raw-sp cap too), so every load stays within [lo, rend) regardless of layout. This test keeps the path exercised: ~160k small string allocations force repeated collections (threshold 256KB, doubling) while a reader coroutine parks on stdin EOF via the fd-wait integration.

## Apply To
* EmperorPenguin Pass3

## Test Code
```
initial {
    let n : mut i64 = 0;
    let total : mut i64 = 0;
    while (n < 20000) {
        let s : mut string = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        let j : mut i64 = 0;
        while (j < 8) {
            s = s + "bbbbbbbbbbbbbbbb";
            j = j + 1;
        }
        total = total + string_length(s);
        n = n + 1;
    }
    println(cast<string>(total));
}

initial {
    while (true) {
        __builtin._fd_wait_read(0);
        let c : string = __builtin._read_fd(0);
        if (c == "") {
            break;
        }
    }
}
```

## Compile
Args: `--enable-coroutine`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
ExpectedExitCode: 0
ExpectedStdout: EQUALS `3200000
`
ExpectedStderr: DISCARD
