#!/usr/bin/env python3
# Merge /tmp/gcmat/results.tsv (unprofiled timings) with stats.tsv (one
# GC_PROFILE probe per scenario x scheduler) into the median table.
import statistics, collections

rows = collections.defaultdict(lambda: {"el": [], "crash": 0, "wrong": 0, "n": 0})
stats = {}
order = []
with open("/tmp/gcmat/results.tsv") as f:
    next(f)
    for line in f:
        p = line.rstrip("\n").split("\t")
        scn, sched, cfg, it, el = p[0], p[1], p[2], p[3], p[4]
        key = (scn, sched, cfg)
        if key not in rows:
            order.append(key)
        r = rows[key]
        if el.startswith("CRASH"):
            r["crash"] += 1
            continue
        r["n"] += 1
        if p[5].startswith("WRONG"):
            r["wrong"] += 1
        r["el"].append(float(el))
try:
    with open("/tmp/gcmat/stats.tsv") as f:
        next(f)
        for line in f:
            p = line.rstrip("\n").split("\t")
            stats[(p[0], p[1])] = p[2:]
except FileNotFoundError:
    pass

sched_order = {"classic": 0, "mt4": 1, "mt8": 2}
cfg_order = {"TIGHT": 0, "NOGC": 1, "RELAX": 2}
order.sort(key=lambda k: (k[0], sched_order.get(k[1], 9), cfg_order.get(k[2], 9)))

hdr = (f'{"scenario":<11}{"sched":<8}{"config":<8}{"elapsed":>8}   '
       f'{"fulls":>6}{"mark_ms":>9}{"sweep_ms":>9}{"gc_ms":>8}{"livepk":>8}  status')
print(hdr)
print("-" * len(hdr))
for k in order:
    r = rows[k]
    st = stats.get((k[0], k[1]), ["-", "-", "-", "-", "-", "-"])
    if r["n"] == 0:
        print(f"{k[0]:<11}{k[1]:<8}{k[2]:<8}{'CRASH':>8}   {st[0]:>6}{st[1]:>9}{st[2]:>9}{st[3]:>8}{st[5]:>8}  CRASH({r['crash']})")
        continue
    el = statistics.median(r["el"])
    status = "ok"
    if r["crash"]:
        status = f"MIXED({r['crash']}crash)"
    if r["wrong"]:
        status += f" WRONGCS({r['wrong']})"
    print(f"{k[0]:<11}{k[1]:<8}{k[2]:<8}{el:>8.0f}   {st[0]:>6}{st[1]:>9}{st[2]:>9}{st[3]:>8}{st[5]:>8}  {status}")
