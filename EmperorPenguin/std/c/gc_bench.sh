#!/usr/bin/env bash
# `make gc-bench` driver: build gc_torture in release (-O2, no sanitizers) and
# run the benchmark workloads under each collector mode, tabulating the
# GC_PROFILE phase-timing line (gc.c). Environment overrides:
#   GC_BENCH_MODES="precise greentea"   collector modes to compare
#   GC_BENCH_WORKLOADS="churn ..."      subset of workloads
#   GC_BENCH_OUT=<dir>                  build/run scratch dir (default: mktemp)
#   GC_BENCH_BIN=<path>                 reuse a prebuilt gc_torture binary
# Workload list is duplicated in gc_torture.c — keep them in sync.
set -u

HERE=$(cd "$(dirname "$0")" && pwd)
MODES=${GC_BENCH_MODES:-precise}
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

printf "%-12s %-12s %7s %7s %7s %7s %9s %8s %10s %8s\n" \
    workload mode majors mark_ms sweep_ms gc_tot_ms minors allocs livepk_MB

run_workload() {
    W=$1; M=$2
    LINE=$(GC_PROFILE=1 EMPEROR_GC_MODE="$M" "$BIN" bench "$W" 2>&1 >/dev/null | grep '^gc_profile:' | tail -1)
    if [ -z "$LINE" ]; then
        printf "%-12s %-12s %s\n" "$W" "$M" "(no gc_profile line — run failed?)"
        return 1
    fi
    get() { echo "$LINE" | tr ' ' '\n' | sed -n "s/^$1=//p"; }
    FULLS=$(get fulls)
    GENFULLS=$(get genfulls)
    MAJORS=$((FULLS + GENFULLS))
    MINORS=$(get minors)
    MARK=$(get mark_ms)
    SWEEP=$(get sweep_ms)
    TOTAL=$(get gc_total_ms)
    ALLOCS=$(get allocs)
    LIVEPK=$(get live_peak_bytes | awk '{ printf "%.1f", $1/1048576 }')
    printf "%-12s %-12s %7s %7s %7s %7s %9s %8s %10s %8s\n" \
        "$W" "$M" "$MAJORS" "$MARK" "$SWEEP" "$TOTAL" "$MINORS" "$ALLOCS" "$LIVEPK"
}

RC=0
for W in $WORKLOADS; do
    for M in $MODES; do
        run_workload "$W" "$M" || RC=1
    done
    echo
done
exit $RC
