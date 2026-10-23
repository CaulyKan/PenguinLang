#!/usr/bin/env bash
# GC matrix over the five scenarios, classic single-heap collector.
# Axes: scheduler (classic / mt4 / mt8) x config (TIGHT/NOGC/RELAX).
#
# Timing runs are UNPROFILED (product truth) — GC_PROFILE costs per-
# allocation atomics and would tax exactly the thing being measured.
# GC statistics (fulls/mark/sweep/live_peak) come from a separate,
# untimed probe run per (scenario, scheduler) written to stats.tsv.
set -u
cd /tmp/gcmat
OUT=results.tsv
STATS=stats.tsv
: > $OUT; : > $STATS
printf "scenario\tsched\tconfig\titer\telapsed_ms\tchecksum\n" >> $OUT
printf "scenario\tsched\tfulls\tmark_ms\tsweep_ms\tgc_ms\talloc_MB\tlive_peak_MB\n" >> $STATS

declare -A EXPECTED=(
  [tbm_local]=4927999984000000
  [tbm_shared]=1159372796160006
  [tbm_live]=2372821382123520
  [tbm_large]=76832768079992
  [tbm_app]=216089101702500
)
TIGHT="EMPEROR_GC_YOUNG=8388608"

run_one() { # $1 scenario $2 bin $3 pin $4 config $5 iter $6... env
  local scn=$1 bin=$2 pin=$3 cfg=$4 it=$5; shift 5
  local out err
  out=$(mktemp); err=$(mktemp)
  taskset -c $pin env "$@" ./bin/$bin > $out 2> $err
  local ec=$?
  local line el cs
  line=$(head -1 $out)
  el=$(echo "$line" | sed -n 's/.*elapsed_ms=\([0-9]*\) .*/\1/p')
  cs=$(echo "$line" | sed -n 's/.*checksum=\([0-9]*\).*/\1/p')
  if [ $ec -ne 0 ] || [ -z "$el" ]; then
    printf "%s\t%s\t%s\t%s\tCRASH(%d)\t-\n" "$scn" "$sched" "$cfg" "$it" "$ec" >> $OUT
    rm -f $out $err; return
  fi
  if [ "$cs" != "${EXPECTED[$scn]}" ]; then cs="WRONG($cs)"; fi
  printf "%s\t%s\t%s\t%s\t%s\t%s\n" "$scn" "$sched" "$cfg" "$it" "$el" "$cs" >> $OUT
  rm -f $out $err
}

probe_one() { # $1 scenario $2 bin $3 pin $4... env — GC_PROFILE stats row
  local scn=$1 bin=$2 pin=$3; shift 3
  local out err
  out=$(mktemp); err=$(mktemp)
  taskset -c $pin env GC_PROFILE=1 "$@" ./bin/$bin > $out 2> $err
  local ec=$?
  if [ $ec -ne 0 ]; then
    printf "%s\t%s\tCRASH(%d)\t-\t-\t-\t-\t-\n" "$scn" "$sched" "$ec" >> $STATS
    rm -f $out $err; return
  fi
  local fulls mark sweep gct alloc live
  fulls=$(sed -n 's/.*fulls=\([0-9]*\).*/\1/p' $err | tail -1)
  mark=$(sed -n 's/.*mark_ms=\([0-9.]*\).*/\1/p' $err | tail -1)
  sweep=$(sed -n 's/.*sweep_ms=\([0-9.]*\).*/\1/p' $err | tail -1)
  gct=$(sed -n 's/.*gc_total_ms=\([0-9.]*\).*/\1/p' $err | tail -1)
  alloc=$(sed -n 's/.*alloc_bytes=\([0-9]*\).*/\1/p' $err | tail -1 | awk '{printf "%.0f", $1/1048576}')
  live=$(sed -n 's/.*live_peak_bytes=\([0-9]*\).*/\1/p' $err | tail -1 | awk '{printf "%.1f", $1/1048576}')
  printf "%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n" "$scn" "$sched" "$fulls" "$mark" "$sweep" "$gct" "$alloc" "$live" >> $STATS
  rm -f $out $err
}

# ---------------- classic (pin 0-7) ----------------
sched=classic
for scn in tbm_local tbm_shared tbm_live tbm_large tbm_app; do
  for it in 1 2 3; do run_one $scn ${scn}_c 0-7 TIGHT $it env $TIGHT; done
  run_one $scn ${scn}_c 0-7 NOGC 1 env EMPEROR_GC_DISABLE=1
  run_one $scn ${scn}_c 0-7 RELAX 1 env
  probe_one $scn ${scn}_c 0-7 env $TIGHT
done

# ---------------- mt4 (pin 0-7) ----------------
sched=mt4
for scn in tbm_local tbm_shared tbm_live tbm_large tbm_app; do
  for it in 1 2 3; do run_one $scn ${scn}_m 0-7 TIGHT $it env $TIGHT EMPEROR_THREADS=4; done
  run_one $scn ${scn}_m 0-7 NOGC 1 env EMPEROR_THREADS=4 EMPEROR_GC_DISABLE=1
  run_one $scn ${scn}_m 0-7 RELAX 1 env EMPEROR_THREADS=4
  probe_one $scn ${scn}_m 0-7 env $TIGHT EMPEROR_THREADS=4
done

# ---------------- mt8 probes (pin 0-11) ----------------
sched=mt8
for scn in tbm_live tbm_app; do
  for it in 1 2 3; do run_one $scn ${scn}_m 0-11 TIGHT $it env $TIGHT EMPEROR_THREADS=8; done
  probe_one $scn ${scn}_m 0-11 env $TIGHT EMPEROR_THREADS=8
done
echo MATRIX_DONE
