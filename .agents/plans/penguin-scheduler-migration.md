# Evaluation: move the coroutine scheduler from C into PenguinLang

Status: evaluation only (no code changed). Scope: `EmperorPenguin/std/c/scheduler.c` (951 lines)
vs a Penguin-side policy layer in `EmperorPenguin/std/penguin/core_builtin.penguin` (1695 lines,
already hosting the poller half: `IFuture`, `_TimerWait`, `__SpawnFuture`, channels, `Event`).

## 1. What scheduler.c actually contains

| # | Piece | Lines (approx) | Nature | Can move to Penguin? |
|---|-------|-------|--------|----------------------|
| 1 | Context switch (`co_switch_to_sched`/`sched_switch_to_co`, trampolines, `co_create`, `co_destroy`) | ~230 | ucontext (POSIX) / Win32 fibers; mmap stack alloc; per-platform `#ifdef` | **No** — platform mechanism |
| 2 | GC interop (scan-region add/remove/set-live, main-stack watermark, `on_coroutine` flag) | woven into 1, 4 | conservative-GC protocol with gc.c | **No** — must live where the switch lives |
| 3 | Park primitives (`_emperor_co_wait`, `fd_park_current`'s sp-capture + switch) | ~80 | reads current sp, narrows GC live range, switches | **No** — needs the raw stack pointer |
| 4 | try/catch sjlj (`_try_buf/_try_setup/_try_leave/throw*`, per-site jmp_buf table) | ~90 | setjmp/longjmp; `__sjlj_setjmp` is an emitter-inlined marker | **No** — orthogonal to scheduling policy anyway |
| 5 | Entry shims (`co_entry_iface` vtable dispatch, `co_entry_fn0`, `_emperor_co_spawn_*`) | ~40 | C↔Penguin object ABI | **No** (vtable lookup from C) — but see spawn-inbox below |
| 6 | Ready / next-round queues (`enqueue`/`dequeue`, linked lists) | ~30 | pure data structure | **Yes** |
| 7 | Timer table (`timer_insert`, `timers_fire`; sorted array) | ~35 | pure data structure | **Yes** |
| 8 | Sim clock / round / activity / settled (`_emperor_sim_*`) | ~10 | plain counters | **Yes** (becomes ordinary Penguin globals + functions) |
| 9 | fd waiter registry + `fd_poll_all` (poll() syscall / Windows probe loop, waiter unlink) | ~170 | syscall half = mechanism; waiter bookkeeping = data | **Split**: syscall + waiter list stay C; the *decisions* (probe-with-0 vs block-with--1, starvation policy) move |
| 10 | Quiescence fingerprint (`fp_hash_bytes`/`sched_fingerprint`) | ~35 | raw-byte hash over frozen stacks | **Split**: per-coroutine hashing stays C (`_co_fingerprint(h)`); the two-round combine rule moves |
| 11 | The delta-round loop (`_emperor_sched_run`) — run ready queue, rotate next queue, fire timers, clock advance, fd-block decision, quiescence exit, exit-code plumbing, cleanup | ~150 | **pure policy** (mirrors BabyPenguin `SimScheduler.cs`) | **Yes** — this is the heart of the migration |
| 12 | Sequential fallback (no ucontext/fibers) | ~60 | portability escape hatch | Keep whole in C (see risks) |

By line count the movable share is ~25–30% (≈260 of 951 lines leave C; C settles around ~650–700
including platform ifdefs and comments). By *semantics*, everything that defines the language's
concurrency model — delta rounds, park/resume discipline, virtual-time advance, fd event
injection, quiescence detection — is policy and moves. What remains in C is a "coroutine context
microkernel": ~15 extern functions. That ceiling is architectural: stackful coroutines + a
conservative GC + sjlj exceptions require raw stack/registers, and those live below any safe
Penguin abstraction.

## 2. Why the move is feasible (the load-bearing argument)

The scheduler loop runs on the **main stack**, never on a coroutine stack. Today main() calls
`_emperor_sched_run()` (C); the loop switches into coroutines and each park switches back into
the current iteration's frame. A Penguin function can play exactly that role:

- The emitter already owns the call site: `LLVMEmitter.penguin:1245` emits
  `call i32 @_emperor_sched_run()` in suspend mode. It would instead call a Penguin function
  `__builtin.__sched_run()` (compiled from core_builtin.penguin like everything else).
- Each round the Penguin loop calls a new extern `_co_switch_in(handle) -> i64 status`
  (`0`=finished, `1`=round-parked, `2`=fd-parked, `3`=exit-requested). The C wrapper contains the
  existing GC protocol verbatim — `setjmp` register flush, `_emperor_gc_main_watermark =
  &flush_buf`, `_emperor_gc_on_coroutine = 1` — then `swapcontext`. When a coroutine parks, the
  switch lands back inside that same C wrapper frame, which returns to the Penguin loop.
  Callee-saved registers are preserved by swapcontext; the Penguin loop's own stack frame is
  untouched while a coroutine runs. Nothing about returns_twice applies (the Penguin frame never
  crosses a setjmp).
- GC correctness is unchanged: the watermark sits at the C wrapper's frame, *deeper* than the
  Penguin loop's frames, so a collection triggered on a coroutine stack scans the main stack from
  the top down to the watermark — covering the Penguin loop's live references (queues, timer
  list).
- Coroutine objects cross the boundary as opaque handles: `class __Coroutine { impl
  IReferenceType; }` — the exact `__TryBuf` pattern already in core_builtin.penguin:62. Handles
  point at C malloc/mmap memory, not GC heap objects, so a conservative scan ignores them; no
  new GC root is needed for the queues.

A key observation that lowers risk considerably: the quiescence **fingerprint never needs to be
stable across versions** — it is only compared between two consecutive rounds inside one process.
So the Penguin-side reimplementation may mix hash values differently from the C version with zero
behavioral impact.

## 3. Target split

### C side keeps (mechanism; ~15 externs)

Unchanged: `_co_wait`, `_fd_wait_read/_fd_wait_write`, `_sched_exit`, all try/catch externs,
`__throw_*`.

New/reshaped:
- `_co_spawn_entry(obj) -> __Coroutine` / `_co_spawn_fn0(fn) -> __Coroutine` — create, do **not**
  enqueue; plus a tiny C-side "spawn inbox" so main() (which passes a raw `ptr @func`) can keep
  calling a C symbol; the Penguin loop drains the inbox at round start.
- `_co_switch_in(h) -> i64 status` — embeds the whole main-stack GC protocol.
- `_co_destroy(h)`, `_co_fingerprint(h) -> u64`, `_co_seq(h) -> i64` (or Penguin tracks order).
- `_fd_waiter_count() -> i64`, `_fd_poll(timeout_ms) -> i64` (count woken), `_fd_take_woken() ->
  i64` (drain woken handles one by one; Penguin enqueues them itself). The waiter list stays in C
  because `fd_park_current` (running on the coroutine stack) must register itself synchronously.
- `_sched_exit_code() -> i64`.

### Penguin side gains (~150–200 lines in `__builtin`, core_builtin.penguin)

- `fun __sched_run() -> i64` — the full delta-round loop, structurally 1:1 with
  `SimScheduler.cs` (the reference implementation): drain spawn inbox → run everything queued at
  round start (parks go to next-round queue) → rotate queues → fire due timers → clock-advance
  decision → fd probe/block decision → quiescence two-fingerprint exit → cleanup + exit code.
- Ready/next queues, creation-order list: `List<__Coroutine>` (order-preserving; creation order
  is what the fingerprint iteration needs).
- Sorted timer list in Penguin; `_timer_at` becomes a real function (extern disappears).
- Sim clock/round/activity/settled become Penguin globals; `_sim_now/_sim_delta/_sim_activity/
  _sim_settled` become real functions — **removing 5 extern/FFI calls entirely**, and the hot
  poller path (`_TimerWait.poll`, channel pollers, `Event.emit` → `_sim_activity`) becomes
  ordinary same-module calls.

### Emitter change (small)

- `LLVMEmitter.penguin:1245`: call the Penguin `__sched_run` instead of `@_emperor_sched_run`
  (or keep the C symbol as the sequential-fallback entry and select by a flag).
- Initial-routine spawning (`:1229`) keeps calling `_emperor_co_spawn_fn0`; C parks the handle in
  the inbox. (Passing a Penguin function *value* to spawn from Penguin code is a possible later
  refinement — needs the function-reference ABI verified; the inbox avoids blocking on that.)

## 4. Risks / hard parts

1. **All-or-nothing state move.** The C loop reads every piece of state (queues, timers, clock,
   activity, fd list). Any state that moves must take its reader — the loop — with it. So this is
   one atomic migration of the policy layer, not incremental cherry-picking. Mitigation: land the
   Penguin loop with the old C loop still present behind a build/emitter switch, A/B the full
   test matrix, then delete the C loop.
2. **Sequential fallback.** The no-ucontext/fiber path's `_co_wait` time-travels by reading
   `timer_ticks[0]` — invisible once timers are Penguin-side. Recommendation: keep the fallback
   entirely in C (a small `_sched_run_sequential`); it is a portability escape hatch, not a
   shipped configuration (Linux and Windows both take the real-switch paths today).
3. **exit() plumbing.** `_emperor_sched_exit` currently sets C flags and switches back; the loop
   reads them. Reshape: switch-in returns status `3`, Penguin reads `_sched_exit_code()` and
   returns. Straightforward but must keep the "exit from main stack exits immediately" half.
4. **fd-parked coroutines must never be destroyed while parked.** Today the state machine makes
   fd-parked cos unreachable for `co_destroy` (they are unlinked only by `fd_poll_all`). In the
   Penguin loop this becomes an explicit `switch (status)` branch — keep the invariant that
   status `2` leaves the handle out of every queue.
5. **GC root for the throw message.** `_emperor_sched_run` registers `_emperor_throw_msg` as a GC
   root (`scheduler.c:815`). The Penguin `__sched_run` cannot do that directly; move the
   registration to C init (first spawn, or emitter main prologue), or expose `_gc_add_root`.
6. **Allocation in the loop.** The Penguin loop allocates (queue nodes, timer inserts). This can
   trigger GC while all coroutines are parked — that is exactly the safe configuration (it is
   also what the fingerprint rounds already do). Only cost: steady-state servers (the Penguin
   LSP parked on stdin) will collect slightly more often; no correctness impact since the loop's
   own frames are scanned via the main stack.
7. **Bootstrap.** core_builtin.penguin is also compiled by BabyPenguin (pass1 compiles
   EmperorPenguin with it). The new loop is ordinary while/List code in the style of the existing
   `_TimerWait`/desugar shims, and unreferenced extern declarations are free — expected to be a
   non-issue, but pass1/pass2/pass3 must all be rebuilt and the full matrix re-run.
8. **Windows fibers.** The scheduler-home fiber (`ConvertThreadToFiber`) and the register-spill
   trick live in the C switch wrapper — untouched by the migration. No Penguin-side impact.

## 5. Verification surface (already exists)

- `Tests/AsyncTest/*`, `Tests/ChannelTest/*`, `Tests/PortTest/*` — delta-round + quiescence semantics.
- `Tests/LspTest/FdEchoChunks.md`, `FdTimerCoexist.md` and `Tests/BasicTest/GcCollectWhileFdParked.md`
  — fd event injection + GC-on-parked-stack (the exact interop this migration must not disturb).
  `MagellanicPenguin/LspServer/StdioStream.penguin` is the production stdin-parked program.
- BabyPenguin itself is the semantic reference (`SimScheduler.cs`) and participates in the same
  byte-exact markdown suite — cross-implementation differential testing comes for free.
- The pass5 md5-convergence bootstrap check guarantees the self-hosted compiler still reproduces
  bit-identically after the emitter/core_builtin changes.

## 6. Recommended path

1. **Phase 1 (parallel, switchable):** implement the C microkernel externs + Penguin `__sched_run`
   in core_builtin.penguin; emitter selects old/new via a flag (compile arg or env). Keep C loop.
2. **Phase 2 (A/B + cut over):** run the full markdown matrix + `make bootstrap` convergence with
   both schedulers; flip the default; keep one release cycle of escape hatch.
3. **Phase 3 (delete):** remove the C delta-round loop, timer table, sim counters (~260 lines);
   scheduler.c shrinks to the microkernel.
4. **Phase 4 (optional, low priority):** move the fd waiter registry into Penguin (needs an
   array-of-fds extern ABI — `#address_of` on an `i64` array); shave another ~80 C lines at the
   cost of FFI complexity. Not recommended initially.

Expected end state: every *policy* line of the scheduler is PenguinLang; C keeps only the
context-switch/GC/poll/sjlj microkernel that cannot be expressed safely above raw stacks.
