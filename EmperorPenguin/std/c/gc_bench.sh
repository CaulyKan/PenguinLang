#!/usr/bin/env bash
# `make gc-bench` driver: build gc_torture in release (-O2, no sanitizers) and
# run the benchmark workloads, tabulating the GC_PROFILE phase-timing line
# (gc.c). Environment overrides:
#   GC_BENCH_WORKLOADS="churn ..."   subset of workloads
#   GC_BENCH_OUT=<dir>               build/run scratch dir (default: mktemp)
#   GC_BENCH_BIN=<path>              reuse a prebuilt gc_torture binary
# Workload list is duplicated in gc_torture.c — keep them in sync.
set -u

HERE=$(cd "$(dirname "$0")" && pwd)
WORKLOADS=${GC_BENCH_WORKLOADS:-churn ptrdense container deepsurvive finstorm mixed512}
OUT=${GC_BENCH_OUT:-}
BIN=${GC_BENCH_BIN:-}
CC=${CC:-clang}

if [ -z "$OUT" ]; then
    OUT=$(mktemp -d)
    trap 'rm -rf "$OUT"' EXIT
fi
mkdir -p "$OUT"

if [ -z "$BIN" ]; then
    BIN="$OUT/gc_torture_bench"
    "$CC" -I"$HERE/../include" -O2 -o "$BIN" \
        "$HERE/gc_torture.c" "$HERE/gc.c" "$HERE/gc_span.c" "$HERE/scheduler.c" "$HERE/penguinlang_interop.c" \
        || { echo "gc_bench: build failed" >&2; exit 1; }
fi

printf "%-12s %7s %7s %7s %9s %8s %10s\n" \
    workload majors mark_ms sweep_ms gc_tot_ms allocs livepk_MB

run_workload() {
    W=$1
    LINE=$(GC_PROFILE=1 "$BIN" bench "$W" 2>&1 >/dev/null | grep '^gc_profile:' | tail -1)
    if [ -z "$LINE" ]; then
        printf "%-12s %s\n" "$W" "(no gc_profile line — run failed?)"
        return 1
    fi
    get() { echo "$LINE" | tr ' ' '\n' | sed -n "s/^$1=//p"; }
    MAJORS=$(get fulls)
    MARK=$(get mark_ms)
    SWEEP=$(get sweep_ms)
    TOTAL=$(get gc_total_ms)
    ALLOCS=$(get allocs)
    LIVEPK=$(get live_peak_bytes | awk '{ printf "%.1f", $1/1048576 }')
    printf "%-12s %7s %7s %7s %9s %8s %10s\n" \
        "$W" "$MAJORS" "$MARK" "$SWEEP" "$TOTAL" "$ALLOCS" "$LIVEPK"
}

RC=0
for W in $WORKLOADS; do
    run_workload "$W" || RC=1
done
exit $RC
