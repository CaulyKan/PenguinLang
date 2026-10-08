/* EmperorPenguin coroutine runtime — the context-switch MICROKERNEL only.
 *
 * The scheduler POLICY (delta rounds, ready/next queues, the timer table,
 * the virtual clock, fd block/probe decisions, quiescence detection) lives
 * in PenguinLang now: __builtin.__sched_run in std/penguin/scheduler.penguin
 * (auto-loaded only with --enable-coroutine, compiled like every other
 * Penguin function, mirroring the BabyPenguin SimScheduler semantics — the
 * reference implementation). The emitter's emit_main calls the compiled
 * loop directly (@__builtin___sched_run). What remains in this file is the
 * machinery that cannot be expressed safely above raw stacks:
 *
 *   - stackful coroutine creation/destruction (ucontext on POSIX, Win32
 *     fibers on Windows) with mmap'd stacks;
 *   - the conservative-GC switch protocol: each stack block is a scan
 *     region, and gc.c scans the MAIN stack only up to the watermark
 *     recorded at the last switch when a collection triggers on a
 *     coroutine stack (see _emperor_gc_alt_stack_scan);
 *   - park primitives (`wait`, fd parks) that capture the raw stack
 *     pointer and switch;
 *   - the poll() syscall half of the external fd event sources (the
 *     waiter bookkeeping stays here because fd_park_current, running on
 *     the coroutine stack, must register itself synchronously);
 *   - try/catch sjlj and the throw entry points;
 *   - per-coroutine frozen-stack hashing for the Penguin-side quiescence
 *     fingerprint.
 *
 * Protocol between the two halves (all externs from core_builtin.penguin):
 *   _co_spawn_entry/_co_spawn_fn0 create a coroutine and park the handle in
 *     a C-side spawn INBOX (nothing is queued — the Penguin loop owns every
 *     queue); _co_spawn_count/_co_take_spawn drain it FIFO;
 *   _co_switch_in(h) runs/resumes h with the whole main-stack GC protocol
 *     embedded, and returns a status: 0 finished, 1 round-parked (re-queue
 *     for the next delta round), 2 fd-parked (in the C waiter list — leave
 *     it out of every queue; _fd_poll re-queues it), 3 exit-requested
 *     (read _sched_exit_code and return it);
 *   _fd_poll(timeout_ms) polls the registered descriptors (0 = readiness
 *     probe, <0 = block until an event); ready waiters are unlinked and
 *     staged, _fd_take_woken hands them to the Penguin loop one by one;
 *   _co_fingerprint(h) hashes h's frozen stack ([parked sp, stack top]);
 *   _co_destroy(h) frees a coroutine (from any state, incl. fd-parked).
 *
 * Sequential fallback (no ucontext/fibers — a portability escape hatch,
 * not a shipped configuration): _co_switch_in runs the entry INLINE on the
 * main stack to completion (wait is a no-op park) and always reports
 * "finished"; the Penguin loop is the same loop on every platform. Because
 * the clock and timers live in Penguin-land, the fallback's _co_wait
 * time-travels by calling back into the compiled helper
 * __builtin___sim_time_travel (the only C→Penguin call in the runtime).
 */

#include "emperor_gc.h"
#include "emperor_interop.h"
#include "emperor_string.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <setjmp.h>
#include <stdarg.h>

#if defined(_WIN32)
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0502
#endif
#include <windows.h>
#include <io.h>
#define EMPEROR_WIN_FIBERS 1
// mingw-w64's setjmp.h declares setjmp/longjmp (and _setjmp via __mingw_setjmp
// macros) but NOT the raw _longjmp symbol MSVC headers expose — use longjmp.
#define _longjmp longjmp
#else
#include <ucontext.h>
#include <sys/mman.h>
#include <poll.h>
#include <time.h>
#define EMPEROR_UCONTEXT 1
#endif

#define EMPEROR_CO_STACK_SIZE (32 * 1024 * 1024)
#ifdef EMPEROR_WIN_FIBERS
/* Fiber stacks: reserve the same 32 MB as the POSIX mmap blocks (parser
 * recursion), but commit only a small seed — the guard page grows the
 * committed range on demand, and the GC only ever scans the live range
 * [parked sp, stack top], which always lies inside committed pages. */
#define EMPEROR_CO_STACK_COMMIT (256 * 1024)
#endif

#if !defined(EMPEROR_UCONTEXT) && !defined(EMPEROR_WIN_FIBERS)
/* Penguin-side clock/timer time-travel helper (sequential fallback only —
 * the sim clock and timer table are globals in core_builtin.penguin, out
 * of C's reach). Defined here so the exact mangled symbol is documented in
 * one place next to its only caller. */
extern void __builtin___sim_time_travel(void);
#endif

/* ---- GC interop (M3: per-thread state + thread registry) ----
 * The switch watermark and on-coroutine flag are PER-THREAD state (a
 * collection on thread A must cover A's main stack up to A's own watermark,
 * never another thread's): C11 _Thread_local. gc.c/core_builtin.c reach them
 * through the accessors below (TLS objects must not be re-declared with
 * plain extern in other TUs). Non-NULL while coroutines exist: when a
 * collection triggers on a coroutine stack, gc scans the thread's main
 * stack only up to this watermark (recorded, with a setjmp register flush,
 * each time the scheduler switches away from it) instead of scanning down
 * to the coroutine's stack pointer, which lives in an unrelated mmap
 * block. */
static _Thread_local int tls_on_coroutine = 0;

int _emperor_sched_am_on_coroutine(void) { return tls_on_coroutine; }

/* gc.c: narrow a registered scan region to its live range. */
void _emperor_gc_scan_set_live(void* base, void* live_lo);

/* ---- cross-platform mutex + condition variable ----
 * Windows: SRWLOCK + CONDITIONVariable (kernel32 — the no-winpthread link
 * constraint holds). POSIX: pthread (in libc on modern glibc). */
#if defined(EMPEROR_WIN_FIBERS)
typedef SRWLOCK mt_mutex;
typedef CONDITION_VARIABLE mt_cond;
#define MT_MUTEX_INIT(m) do { InitializeSRWLock(&(m)); } while (0)
#define MT_COND_INIT(c) do { InitializeConditionVariable(&(c)); } while (0)
#define MT_LOCK(m) AcquireSRWLockExclusive(&(m))
#define MT_UNLOCK(m) ReleaseSRWLockExclusive(&(m))
#define MT_TRYLOCK(m) (TryAcquireSRWLockExclusive(&(m)) ? 0 : 1) /* 0 ok, 1 busy */
#define MT_WAIT(c, m) SleepConditionVariableSRW(&(c), &(m), INFINITE, 0)
#define MT_SIGNAL(c) WakeConditionVariable(&(c))
#define MT_BROADCAST(c) WakeAllConditionVariable(&(c))
#else
#include <pthread.h>
typedef pthread_mutex_t mt_mutex;
typedef pthread_cond_t mt_cond;
#define MT_MUTEX_INIT(m) do { pthread_mutex_init(&(m), NULL); } while (0)
#define MT_COND_INIT(c) do { pthread_cond_init(&(c), NULL); } while (0)
#define MT_LOCK(m) pthread_mutex_lock(&(m))
#define MT_UNLOCK(m) pthread_mutex_unlock(&(m))
#define MT_TRYLOCK(m) pthread_mutex_trylock(&(m))
#define MT_WAIT(c, m) pthread_cond_wait(&(c), &(m))
#define MT_SIGNAL(c) pthread_cond_signal(&(c))
#define MT_BROADCAST(c) pthread_cond_broadcast(&(c))
#endif

/* Uniform relaxed-atomic accessors for fields shared between the locked
 * protocol and the lock-free spin hints: once a location is accessed
 * atomically anywhere, EVERY access must be atomic (mixed atomic/plain
 * access is itself a data race — the residue TSan reported after the first
 * atomicization round). Relaxed: the mt_glock handshake or the spin's
 * locked re-check provides whatever ordering the protocol needs; these
 * only have to be race-free. */
#define MT_ALD(x) __atomic_load_n(&(x), __ATOMIC_RELAXED)
#define MT_AST(x, v) __atomic_store_n(&(x), (v), __ATOMIC_RELAXED)
#define MT_AFA(x, v) __atomic_fetch_add(&(x), (v), __ATOMIC_RELAXED)
#define MT_AFS(x, v) __atomic_fetch_sub(&(x), (v), __ATOMIC_RELAXED)

/* ---- Thread registry (M3 groundwork for the multi-thread scheduler) ----
 * Every thread that touches the scheduler registers here and OWNS one slot:
 * it writes the slot's watermark at each switch (single writer per slot —
 * no lock; plain aligned pointer store). The switch watermark therefore
 * lives in the registry slot, not TLS, so ANY collecting thread can cover
 * every other thread's [base, watermark) — a thread that is not collecting
 * is parked at its delta-boundary watermark (boundary = stop-the-world).
 * Single-threaded today the registry holds only main, and the parked-thread
 * visitor is a no-op — zero behavior change.
 *
 * The CURRENT thread's watermark accessor reads its own slot; when
 * collection happens on main (never switched), the slot value is NULL and
 * gc falls back to the live stack pointer, exactly like the old global. */
#define EMPEROR_SCHED_MAX_THREADS 64
typedef struct EmperorThreadInfo {
    int used;
    void* stack_bottom;  /* gc.c's recorded stack base at init (main) or the
                            thread's own base (workers, future) */
    void* watermark;     /* latest switch-away point on this thread's main
                            stack (NULL until the first switch) */
    /* M4b execution state (see the MT kernel section at the bottom): */
    struct EmperorCoroutine* runq_head; /* runnable FIFO (qnext-linked) */
    struct EmperorCoroutine* runq_tail;
    mt_cond cv;           /* idle wakeups: work routed / delta end / stop */
    int idle;             /* parked at the delta barrier (mt_idle_count) */
    int between_parked;   /* parked in worker_main's between-delta wait
                             (mt_between_count) — counted toward CONVERGENCE
                             (idle + between == nthreads) so a worker may
                             sleep through deltas it has no work for; NOT
                             counted for the post-delta barrier release,
                             which only waits out idle_wait parkers */
    int is_main;
    void* thread_handle;  /* worker join handle */
    /* Diagnostics: delta_loop body iterations (a frozen thread pinned to a
     * queue it can never pop shows a climbing count with zero steps). */
    int64_t loop_iters;
} EmperorThreadInfo;
static EmperorThreadInfo sched_threads[EMPEROR_SCHED_MAX_THREADS];
static int sched_thread_count = 0;
static _Thread_local int tls_thread_index = -1;

/* gc.c: the stack base recorded at _emperor_gc_init. */
void* _emperor_gc_stack_base(void);

/* Component-lock depth (the policy's _sync_begin/_sync_end bracket their
 * critical sections through this). A coroutine inside a component critical
 * section must NEVER valve/STW-park: it would sleep holding the spinlock
 * and the boundary's own commit path (which takes the same lock) would
 * spin forever — the collector deadlock. Park requests from inside a
 * critical section are declined; the poll re-tries at the next safepoint
 * outside it (regions are short, and the loop back-edge polls guarantee
 * one comes). */
static _Thread_local int tls_sync_depth = 0;
void _emperor_mt_sync_depth_add(int d) { tls_sync_depth += d; }

static void sched_thread_register(void) {
    if (tls_thread_index >= 0) return;
    if (sched_thread_count < EMPEROR_SCHED_MAX_THREADS) {
        sched_threads[sched_thread_count].used = 1;
        sched_threads[sched_thread_count].stack_bottom =
            _emperor_gc_stack_base();
        sched_threads[sched_thread_count].watermark = NULL;
        tls_thread_index = sched_thread_count;
        sched_thread_count++;
    }
}

void* _emperor_sched_main_watermark(void) {
    if (tls_thread_index < 0) return NULL;
    return sched_threads[tls_thread_index].watermark;
}

static void sched_thread_set_watermark(void* wm) {
    if (tls_thread_index < 0) return;
    sched_threads[tls_thread_index].watermark = wm;
}

void _emperor_sched_each_parked_main_stack(void (*visit)(void* bottom, void* top)) {
    if (tls_thread_index < 0) return;
    for (int i = 0; i < sched_thread_count; i++) {
        if (i == tls_thread_index || !sched_threads[i].used) continue;
        void* wm = sched_threads[i].watermark;
        if (wm && sched_threads[i].stack_bottom) {
            visit(sched_threads[i].stack_bottom, wm);
        }
    }
}

/* ---- try/catch (sjlj) ---- */

typedef struct EmperorTryFrame {
    struct EmperorTryFrame* prev;
    jmp_buf* jb; /* points into the per-site table; owned by the site */
    /* GC v2: the frame-chain head at try-entry. Throw longjmps past every
     * deeper frame WITHOUT running their epilogues, so their descriptors
     * would stay linked and the precise walk would read dead frames' slots.
     * Restoring this value at throw truncates the chain back to the try
     * function's own state (its frame was already linked when the try was
     * set up). */
    void* saved_gc_frames;
} EmperorTryFrame;

/* Pending error payload for the catch handler: set by throw, read by the
 * catch-entry code. Registered as a GC root (lazily, on first throw — the
 * old registration point was the C scheduler loop, which no longer exists)
 * so the message string survives the longjmp window. */
static _emperor_string* _emperor_throw_msg = NULL;
static int64_t _emperor_throw_code = 0;
static int _emperor_throw_msg_root_done = 0;

/* ---- Coroutine ---- */

enum {
    CO_READY = 1,   /* created, never ran (sitting in the spawn inbox or a
                       Penguin queue) */
    CO_RUNNING = 2, /* currently executing */
    CO_PARKED = 3,  /* stopped in wait(), re-queued for next round */
    CO_FINISHED = 4, /* entry returned; pending free */
    CO_FD_PARKED = 5, /* parked on an fd (fd_waiters list); NOT re-queued —
                         the Penguin loop re-queues it when the fd turns
                         ready (_fd_poll/_fd_take_woken) */
    /* mt-scheduler states (scheduler_mt.penguin policy; unreachable under
     * the classic scheduler — nothing there calls the two parks below). */
    CO_YIELD = 6,   /* yielded execution only: the mt loop re-queues it into
                       the SAME delta (no time passes, no convergence owed) */
    CO_COND = 7,    /* condition park: owed nothing; woken only by the
                       policy/library (channel delivery, timer fire, future
                       completion) re-queueing it */
    CO_RUNNABLE = 8 /* woken from CO_COND by _emperor_mt_wake; sitting in a
                       run queue (the wake's push IS the requeue) */
};

typedef struct EmperorCoroutine {
#if defined(EMPEROR_WIN_FIBERS)
    void* fiber;   /* CreateFiberEx context (system-allocated stack) */
    jmp_buf regs;  /* callee-saved register spill captured at park: while the
                      fiber is switched away, SwitchToFiber parks its
                      registers in scheduler-private memory the conservative
                      GC cannot reach; this buffer (inside the scanned struct)
                      holds the pointer-bearing subset. Never longjmp'd. */
#else
    ucontext_t ctx; /* saved callee-saved registers live here (scanned) */
#endif
    char* block;    /* scan-region base: mmap block base | fiber stack reservation base */
    char* stack_hi; /* region end: block end (just past the struct) | fiber stack top */
    char* sp_park;  /* stack pointer captured at park (fingerprint start) */
    void (*entry)(void*); /* consumed at the coroutine's first switch */
    void* entry_arg;
    struct EmperorCoroutine* qnext; /* spawn inbox / fd-woken list link */
    struct EmperorCoroutine* onext; /* OWED-list link — exclusive with
                                       qnext by construction: the owed list
                                       and the run queues used to share qnext
                                       and a double filing (a wake's phantom
                                       racing the bare-wait filing) linked
                                       one coroutine into BOTH structures,
                                       corrupting their chains (the N=8
                                       double-pop freeze). */
    struct EmperorCoroutine* fnext; /* FUTURES-list link — exclusive: a
                                       future-parked coroutine can be woken
                                       DIRECTLY (timer fire, delivery kick of
                                       another entry... no: kick drains first),
                                       and the wake's run-queue push CLOBBERS
                                       qnext — sharing qnext with the futures
                                       list orphaned every coroutine parked
                                       BEHIND a timer-woken one (the ESL bug) */
    /* GC v2 precise roots: this coroutine's frame-chain head while parked
     * (the running coroutine's frames are linked on
     * _emperor_gc_frame_head; the switch swaps the two). NULL while it has
     * never run or fully unwound. */
    void* gc_frames;
    struct EmperorCoroutine* all_next; /* every live coroutine (GC root walk) */
    int state;
    int stealable; /* Pass 10 verdict: never-run distribution may move it (M4b) */
    int owner_idx;  /* registry slot of the thread that last ran it (set at every
                       switch-in; stable once started — decision A: no migration) */
    int seq;              /* creation index (stable identity for the Penguin
                             loop's all-live registry; read via _co_seq) */
    EmperorTryFrame* try_top;
    /* M4b hardening: run-queue membership. Every push/pop happens under
     * mt_glock, so the flag turns a DOUBLE queue-insert (wake + a requeue
     * path racing the same park) into a no-op instead of a use-after-
     * munmap: the second queue's entry would otherwise pop a coroutine
     * another thread already ran to completion and destroyed. */
    int queued;
    int in_futures; /* linked on mt_futures_head (guarded by mt_glock —
                       lets register be idempotent across the poll that
                       may yield and consume the first registration) */
    int future_park; /* between _mt_future_register and unregister/cancel —
                        gates switch_away's owed rescue to future waits
                        (the Fifo backpressure parks file themselves and
                        must not grow their waiter lists via rescues) */
    int lib_park;    /* between _mt_park_begin and the next resume — the
                        coroutine is (about to be) filed on a PENGUIN-side
                        waiter list the C kernel cannot walk (Fifo
                        backpressure writers); the boundary self-healing
                        must skip it or it would spurious-wake the writer
                        every boundary and pin the tick (the backpressure
                        test's exit 65) */
    int on_owed;     /* linked on mt_owed_head (mt_glock-guarded; the owed
                        list shares qnext with the run queues, so a double
                        owed filing self-links the tail and corrupts both
                        structures — the double-pop freeze) */
} EmperorCoroutine;

/* Cross-thread protocol fields, race-free by construction:
 *   state     — written by the coroutine itself (park paths, switch-in,
 *               entry finish) and read/written by ANY thread's wake under
 *               mt_glock; the park->register-under-glock->wake handshake
 *               provides all the ordering the protocol needs, so these
 *               accesses only have to be data-race-free: RELAXED atomics.
 *   owner_idx — written at switch-in, read by any thread's wake routing. */
static inline void co_state_set(EmperorCoroutine* co, int s) {
    __atomic_store_n(&co->state, s, __ATOMIC_RELAXED);
}
static inline int co_state_get(const EmperorCoroutine* co) {
    return __atomic_load_n(&co->state, __ATOMIC_RELAXED);
}
static inline void co_owner_set(EmperorCoroutine* co, int t) {
    __atomic_store_n(&co->owner_idx, t, __ATOMIC_RELAXED);
}
static inline int co_owner_get(const EmperorCoroutine* co) {
    return __atomic_load_n(&co->owner_idx, __ATOMIC_RELAXED);
}

static _Thread_local EmperorCoroutine* sched_current = NULL; /* NULL on main/scheduler stack; per-thread (M3) */

/* Every live coroutine (intrusive, linked at create, unlinked at destroy).
 * The GC's frame-chain walk needs each PARKED stack's saved head; the
 * queues live in Penguin-land, invisible to C. */
static EmperorCoroutine* co_all_head = NULL;

/* gc.c: mark every ref reachable from one frame chain. The walker skips
 * the RUNNING coroutine — its saved head is stale (its frames are on the
 * current chain). */
void _emperor_sched_each_frame_head(void (*visit)(void*)) {
    for (EmperorCoroutine* co = co_all_head; co; co = co->all_next) {
        if (co != sched_current && co->gc_frames) {
            visit(co->gc_frames);
        }
    }
}

#if defined(EMPEROR_UCONTEXT)
static _Thread_local ucontext_t sched_ctx; /* this thread's scheduler switchback point (M3) */
#elif defined(EMPEROR_WIN_FIBERS)
static _Thread_local void* sched_fiber = NULL; /* this thread's fiber (scheduler home; M3) */
#endif

static int sched_exit_requested = 0; /* MT_ALD/MT_AST only */
static int sched_exit_code = 0;
/* Live coroutines (created minus destroyed) — the mt kernel's exit check. */
static int64_t mt_live_count = 0;

/* ---- Stall diagnostics (design doc ch.12: "two stall classes, never a
 * silent hang") ----
 * (a) a delta that never converges — two coroutines waking each other, or
 *     a `while (!cond) wait -1;` spin (the yield keeps the coroutine
 *     runnable, so convergence never happens);
 * (b) deltas keep converging but the virtual clock never advances while
 *     someone waits on time — the `while (!cond) wait;` retry loop that
 *     pins the tick (bare wait is owed its next delta AT the current
 *     tick, so time cannot jump past it).
 * Both are leash budgets, not detectors: a legitimate long delta (a deep
 * immediate-container pipeline) or a long same-tick churn (a big deferred
 * pipeline) stays orders below the defaults, and EMPEROR_SCHED_STEP_BUDGET
 * / EMPEROR_SCHED_PIN_BUDGET retune or disable (0) them. When a budget is
 * exceeded the scheduler reports to stderr and stops the program with a
 * distinctive exit code (60 = live-lock, 61 = pinned clock) instead of
 * hanging silently forever. */
static int64_t mt_step_count = 0;   /* resumes in the current delta (a) */
static int64_t mt_step_budget = -1; /* lazy env parse; -1 = unparsed */
static int64_t mt_pin_streak = 0;   /* converged deltas without any timer
                                       making real (later-deadline) progress (b) */
static int64_t mt_pin_max_deadline = INT64_MIN; /* latest deadline ever fired */
static int64_t mt_pin_budget = -1;  /* lazy env parse; -1 = unparsed */
static int mt_stall_reported = 0;   /* one diagnostic per run (CAS guard) */

/* ---- Spawn inbox ----
 * co_create parks fresh handles here (FIFO, qnext-linked); the Penguin loop
 * drains them at round boundaries and after every switch — a coroutine
 * spawned during round N may run in round N, exactly like the old C loop's
 * enqueue-into-ready. main() spawns initial routines through
 * _emperor_co_spawn_fn0 before the loop is entered; async spawns from
 * coroutine context land here mid-round. */
/* Per-thread (M3): a spawn lands in the CREATING thread's inbox — the
 * affinity rule's C-side half (the mt policy drains its own inbox). */
static _Thread_local EmperorCoroutine* spawn_inbox_head = NULL;
static _Thread_local EmperorCoroutine* spawn_inbox_tail = NULL;

static void inbox_push(EmperorCoroutine* co) {
    co->qnext = NULL;
    if (spawn_inbox_tail) {
        spawn_inbox_tail->qnext = co;
        spawn_inbox_tail = co;
    } else {
        spawn_inbox_head = co;
        spawn_inbox_tail = co;
    }
}

int64_t _emperor_co_spawn_count(void) {
    int64_t n = 0;
    for (EmperorCoroutine* c = spawn_inbox_head; c; c = c->qnext) n++;
    return n;
}

void* _emperor_co_take_spawn(void) {
    EmperorCoroutine* co = spawn_inbox_head;
    if (!co) return NULL;
    spawn_inbox_head = co->qnext;
    if (!spawn_inbox_head) spawn_inbox_tail = NULL;
    co->qnext = NULL;
    return co;
}

/* ---- Context switches ----
 * POSIX: swapcontext saves the outgoing context's callee-saved registers
 * into co->ctx, which lives inside the GC-scanned stack block.
 * Windows: SwitchToFiber parks them in scheduler-private memory instead, so
 * a setjmp first spills them into the scanned EmperorCoroutine struct — the
 * buffer is never longjmp'd, it exists purely as GC-visible state. */

/* x86-64 fast swap: glibc's swapcontext performs TWO rt_sigprocmask
 * syscalls per switch (the POSIX sigmask save/restore) — measured 1.1M
 * sigprocmask calls in 15 s of tinyriscv churn (~2 per resume, the
 * wake/re-park cycles dominate). This runtime never changes a coroutine's
 * signal mask, so the mask juggling is pure cost. The fast path swaps only
 * what the SysV ABI makes callee-saved (rbx, rbp, r12-r15, rsp, rip) plus
 * the two callee-saved FP control words (MXCSR, x87 CW — full XMM state is
 * caller-saved and needs no preservation across the switch), all inside
 * the SAME ucontext_t layout glibc's getcontext/makecontext prime — the
 * struct stays in GC-scanned memory either way. Other POSIX targets keep
 * the glibc switch. TSan builds keep the glibc switch TOO: its swapcontext
 * interceptor performs the thread-state handoff the analyzer needs, which
 * raw asm would silently skip (false positives on every resume). */
#if defined(EMPEROR_UCONTEXT) && defined(__x86_64__) && defined(__GNUC__) \
    && !(defined(__has_feature) && __has_feature(thread_sanitizer)) \
    && !defined(__SANITIZE_THREAD__)
#define EMPEROR_FAST_SWAP 1
/* glibc guards the greg-index enum (REG_RIP & co) under __USE_GNU; the
 * indices are the fixed x86-64 sigcontext ABI order, so spell the offsets
 * out: gregs[i] lives at 40 + 8*i inside ucontext_t (uc_flags 8 + uc_link 8
 * + uc_stack 24), the fpregs pointer at 40 + 23*8, and in the fxsave area
 * cwd is at 0, mxcsr at 24. */
#define EMP_USTR_(x) #x
/* gregs[i] is at i*8+40 (uc_flags 8 + uc_link 8 + uc_stack 24); the
 * fpregs pointer at 23*8+40 = 224; fxsave cwd at 0, mxcsr at 24. */
#define EMP_UTX_GREG(i) EMP_USTR_(i) "*8+40"
#define EMP_UTX_FPREGS "224"
#define EMP_FP_CWD "0"
#define EMP_FP_MXCSR "24"
void co_swap(ucontext_t* save, ucontext_t* load);
/* Raw assembly (x86 has no __attribute__((naked))): a compiled C wrapper
 * lets the prologue touch the callee-saved registers BEFORE the asm could
 * save them — the exact corruption the first attempt segfaulted on. Entry:
 * rdi = save ucontext, rsi = load ucontext, rsp at the return address. The
 * save side records the caller's return address as RIP and the pre-call
 * rsp, so the resumed stream's jmp lands right after ITS co_swap call and
 * it returns through its own epilogue-free path normally. */
__asm__(
    ".text\n"
    ".globl co_swap\n"
    ".type co_swap,@function\n"
    "co_swap:\n"
    "  endbr64\n"
    "  movq (%rsp), %rax\n"                            /* return address */
    "  movq %rax, " EMP_UTX_GREG(16) "(%rdi)\n"        /* gregs[RIP] */
    "  leaq 8(%rsp), %rax\n"
    "  movq %rax, " EMP_UTX_GREG(15) "(%rdi)\n"        /* gregs[RSP] */
    "  movq %rbx, " EMP_UTX_GREG(11) "(%rdi)\n"
    "  movq %rbp, " EMP_UTX_GREG(10) "(%rdi)\n"
    "  movq %r12, " EMP_UTX_GREG(4) "(%rdi)\n"
    "  movq %r13, " EMP_UTX_GREG(5) "(%rdi)\n"
    "  movq %r14, " EMP_UTX_GREG(6) "(%rdi)\n"
    "  movq %r15, " EMP_UTX_GREG(7) "(%rdi)\n"
    "  movq " EMP_UTX_FPREGS "(%rdi), %rax\n"          /* save fpregs ptr */
    "  testq %rax, %rax\n"
    "  je 1f\n"
    "  stmxcsr " EMP_FP_MXCSR "(%rax)\n"
    "  fnstcw " EMP_FP_CWD "(%rax)\n"
    "1:\n"
    "  movq " EMP_UTX_FPREGS "(%rsi), %rax\n"          /* load fpregs ptr */
    "  testq %rax, %rax\n"
    "  je 2f\n"
    "  ldmxcsr " EMP_FP_MXCSR "(%rax)\n"
    "  fldcw " EMP_FP_CWD "(%rax)\n"
    "2:\n"
    "  movq " EMP_UTX_GREG(11) "(%rsi), %rbx\n"
    "  movq " EMP_UTX_GREG(10) "(%rsi), %rbp\n"
    "  movq " EMP_UTX_GREG(4) "(%rsi), %r12\n"
    "  movq " EMP_UTX_GREG(5) "(%rsi), %r13\n"
    "  movq " EMP_UTX_GREG(6) "(%rsi), %r14\n"
    "  movq " EMP_UTX_GREG(7) "(%rsi), %r15\n"
    "  movq " EMP_UTX_GREG(15) "(%rsi), %rsp\n"
    "  jmp *" EMP_UTX_GREG(16) "(%rsi)\n"
    ".size co_swap, .-co_swap\n");
#define swapcontext(save, load) co_swap((save), (load))
#endif

#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
static void co_switch_to_sched(EmperorCoroutine* self) {
#ifdef EMPEROR_UCONTEXT
    swapcontext(&self->ctx, &sched_ctx);
#else
    setjmp(self->regs);
    SwitchToFiber(sched_fiber);
#endif
}
static void sched_switch_to_co(EmperorCoroutine* co) {
#ifdef EMPEROR_UCONTEXT
    swapcontext(&sched_ctx, &co->ctx);
#else
    SwitchToFiber(co->fiber);
#endif
}

/* Windows: the scheduler runs on the main thread converted to a fiber —
 * every SwitchToFiber must originate from — and return to — a fiber
 * context. (A second call after ConvertThreadToFiber-less nesting sees
 * ERROR_ALREADY_FIBER and reuses the current fiber.) */
static void ensure_sched_home(void) {
#ifdef EMPEROR_WIN_FIBERS
    if (!sched_fiber) {
        sched_fiber = ConvertThreadToFiber(NULL);
        if (!sched_fiber && GetLastError() == ERROR_ALREADY_FIBER) {
            sched_fiber = GetCurrentFiber();
        }
        if (!sched_fiber) {
            fprintf(stderr, "emperor sched: ConvertThreadToFiber failed\n");
            exit(1);
        }
    }
#endif
}
#endif /* real context switches */

/* The coroutine body: runs the recorded entry, then marks the coroutine
 * finished. POSIX: returning afterwards follows uc_link back to the
 * scheduler. Windows: the fiber must not return from its start routine, so
 * the trampoline explicitly switches back (the scheduler then destroys it). */
static void co_run_entry(EmperorCoroutine* self) {
    void (*entry)(void*) = self->entry;
    void* arg = self->entry_arg;
    self->entry = NULL;
    self->entry_arg = NULL;
    /* The async ctx object (arg) was rooted at spawn because this struct
     * held the only reference — the root's job is done once the entry has
     * it on its own frames. */
    _emperor_gc_remove_root(&self->entry_arg);
    entry(arg);
    co_state_set(self, CO_FINISHED);
}

#ifndef EMPEROR_WIN_FIBERS
static void co_trampoline(void) {
    co_run_entry(sched_current);
    /* NEVER rely on returning (uc_link): uc_link is baked at CREATION and
     * points at the CREATOR's TLS sched_ctx — wrong the moment a
     * distributed/stolen coroutine finishes on another thread (the worker
     * would resume MAIN's saved context, writing main's stack from the
     * worker: the M4b canary smash). Switch to THIS thread's scheduler
     * home explicitly; the landing context is dead storage. */
    ucontext_t dead_ctx;
    swapcontext(&dead_ctx, &sched_ctx);
    fprintf(stderr, "emperor sched: trampoline returned after final switch\n");
    abort();
}
#else
/* First code to run on a new fiber: discover the stack's bounds (one
 * VirtualQuery on a local: reservation base, committed base, top) and
 * register the stack as a GC scan region BEFORE any user frame exists. The
 * region's initial live range starts at the committed base — pages of the
 * 32 MB reservation below it are reserved-but-uncommitted and a
 * conservative scan must never touch them; the range then moves to each
 * park's sp, which always lies inside the committed area (and the RUNNING
 * fiber's range is capped at its collect-time sp by gc.c, exactly like the
 * POSIX coroutine blocks). */
static void co_win_trampoline(void* arg) {
    EmperorCoroutine* co = (EmperorCoroutine*)arg;
    MEMORY_BASIC_INFORMATION mbi;
    VirtualQuery((LPCVOID)&mbi, &mbi, sizeof(mbi));
    char* reserve_base = (char*)mbi.AllocationBase;
    char* committed_base = (char*)mbi.BaseAddress;
    char* stack_top = (char*)mbi.BaseAddress + mbi.RegionSize;
    if (reserve_base > committed_base) reserve_base = committed_base;
    co->block = reserve_base;
    co->stack_hi = stack_top;
    _emperor_gc_scan_add(co->block, (size_t)(stack_top - co->block));
    _emperor_gc_scan_set_live(co->block, committed_base);
    co_run_entry(co);
    SwitchToFiber(sched_fiber);
    /* never returns */
}
#endif

static int co_seq_counter = 0;

static EmperorCoroutine* co_create_queued(void (*entry)(void*), int auto_inbox) {
    if (tls_thread_index < 0) sched_thread_register();
    mt_live_count++;
    EmperorCoroutine* co;
#if defined(EMPEROR_UCONTEXT)
    size_t total = EMPEROR_CO_STACK_SIZE;
    char* block = (char*)mmap(NULL, total, PROT_READ | PROT_WRITE,
                              MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (block == MAP_FAILED) {
        fprintf(stderr, "emperor sched: cannot allocate coroutine stack\n");
        abort();
    }
    co = (EmperorCoroutine*)(block + total - sizeof(EmperorCoroutine));
    memset(co, 0, sizeof(EmperorCoroutine));
    co->block = block;
    co->stack_hi = block + total;
    co_state_set(co, CO_READY);
    co->seq = co_seq_counter++;
    co->entry = entry;
    co->entry_arg = NULL; /* set by caller */
    /* Whole block is one GC scan region: live frames, the saved ucontext
     * (spilled registers) and the struct itself are all covered; bytes below
     * the parked sp are stale garbage, which a conservative collector safely
     * over-retains. */
    _emperor_gc_scan_add(block, total);
    /* No frames exist yet: an empty live range (== region end) excludes the
     * all-zero never-used pages from conservative scans entirely; the first
     * park (or the running-sp cap in _emperor_gc_collect) opens it up. */
    _emperor_gc_scan_set_live(block, block + total);
    if (getcontext(&co->ctx) == -1) {
        fprintf(stderr, "emperor sched: getcontext failed\n");
        abort();
    }
    co->ctx.uc_stack.ss_sp = block;
    co->ctx.uc_stack.ss_size = total - sizeof(EmperorCoroutine) - 64;
    co->ctx.uc_link = NULL; /* the trampoline's final swap owns the
                                * return to this thread's scheduler home */
    makecontext(&co->ctx, co_trampoline, 0);
#elif defined(EMPEROR_WIN_FIBERS)
    /* Windows fiber: the struct is heap-allocated (the fiber stack belongs
     * to the system); its bounds — and the stack's scan region — are only
     * discovered when the fiber first runs (co_win_trampoline). From
     * creation the struct itself is the scan region: it holds entry_arg (a
     * Penguin object) until the entry consumes it, and every park's
     * register spill afterwards. */
    co = (EmperorCoroutine*)calloc(1, sizeof(EmperorCoroutine));
    if (!co) {
        fprintf(stderr, "emperor sched: cannot allocate coroutine struct\n");
        abort();
    }
    co->fiber = CreateFiberEx(EMPEROR_CO_STACK_COMMIT, EMPEROR_CO_STACK_SIZE,
                              FIBER_FLAG_FLOAT_SWITCH, co_win_trampoline, co);
    if (!co->fiber) {
        fprintf(stderr, "emperor sched: CreateFiberEx failed (GetLastError %lu)\n",
                (unsigned long)GetLastError());
        free(co);
        abort();
    }
    co_state_set(co, CO_READY);
    co->seq = co_seq_counter++;
    co->entry = entry;
    co->entry_arg = NULL; /* set by caller */
    _emperor_gc_scan_add((char*)co,
                         ((sizeof(EmperorCoroutine) + 7u) & ~(size_t)7u));
#else
    /* Sequential fallback (no real context switch): entries run inline on
     * the main stack inside _co_switch_in, so the struct is the only
     * memory — and the only scan region (entry_arg, a Penguin object, must
     * stay GC-visible until the entry consumes it). */
    co = (EmperorCoroutine*)calloc(1, sizeof(EmperorCoroutine));
    if (!co) {
        fprintf(stderr, "emperor sched: cannot allocate coroutine struct\n");
        abort();
    }
    co->block = (char*)co;
    co->stack_hi = (char*)co + sizeof(EmperorCoroutine);
    co_state_set(co, CO_READY);
    co->seq = co_seq_counter++;
    co->entry = entry;
    co->entry_arg = NULL; /* set by caller */
    _emperor_gc_scan_add((char*)co,
                         ((sizeof(EmperorCoroutine) + 7u) & ~(size_t)7u));
#endif
    co->all_next = co_all_head;
    co_all_head = co;
    if (auto_inbox) {
        inbox_push(co);
    }
    return co;
}

static EmperorCoroutine* co_create(void (*entry)(void*)) {
    return co_create_queued(entry, 1);
}

/* Defined in the MT kernel section below: files a coroutine out of every
 * run queue before it is freed. A wake inside a park window pushes a queue
 * entry for a coroutine that may then run to completion — the entry must
 * not outlive the stack it points at (the use-after-munmap class). */
static void mt_unfile_runq_phantom(EmperorCoroutine* co);

void _emperor_co_destroy(void* handle) {
    EmperorCoroutine* co = (EmperorCoroutine*)handle;
    mt_live_count--;
    mt_unfile_runq_phantom(co);
    /* Unlink from the all-coroutines list before anything is freed: the GC
     * frame walk must not see a destroyed coroutine's saved chain. */
    EmperorCoroutine** pp = &co_all_head;
    while (*pp) {
        if (*pp == co) {
            *pp = co->all_next;
            break;
        }
        pp = &(*pp)->all_next;
    }
    _emperor_gc_remove_root(&co->entry_arg);
    _emperor_gc_scan_remove(co->block); /* NULL: fiber never ran — no stack region */
#if defined(EMPEROR_UCONTEXT)
    munmap(co->block, EMPEROR_CO_STACK_SIZE);
#elif defined(EMPEROR_WIN_FIBERS)
    _emperor_gc_scan_remove((char*)co); /* the struct region (entry_arg, reg spills) */
    if (co->fiber) DeleteFiber(co->fiber);
    free(co);
#else
    free(co);
#endif
}

int64_t _emperor_co_seq(void* handle) {
    return ((EmperorCoroutine*)handle)->seq;
}

/* ---- Run/resume one coroutine (the scheduler loop's only lever) ----
 * Switches into the coroutine on the REAL-switch platforms; the whole
 * main-stack GC protocol lives here. When the coroutine parks (wait, fd
 * wait, exit), the switch lands back inside this same frame and the status
 * is derived from co->state. Callee-saved registers are preserved by the
 * switch itself; the Penguin caller's stack frame is untouched while the
 * coroutine runs. */
int64_t _emperor_co_switch_in(void* handle) {
    EmperorCoroutine* co = (EmperorCoroutine*)handle;
    sched_current = co;
    co->lib_park = 0; /* resumed: any park-window claim is spent; the next
                         park re-claims (park_begin / future_register) */
    co_state_set(co, CO_RUNNING);
    co_owner_set(co, tls_thread_index); /* decision A: first runner owns it forever */
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
    ensure_sched_home();
    /* Flush main-stack callee-saved registers and record the main stack
     * watermark for a possibly-GC-triggering coroutine. */
    jmp_buf switch_flush;
    setjmp(switch_flush);
    if (tls_thread_index < 0) sched_thread_register();
    sched_thread_set_watermark((char*)&switch_flush);
    tls_on_coroutine = 1;
    /* Swap the precise frame-chain head: the coroutine's stack carries its
     * own chain (its gc_frames from the last park, NULL on first run);
     * main's head is restored when the switch lands back and the
     * coroutine's chain is re-saved into its struct. */
    void* saved_main_frames = _emperor_gc_frame_head;
    _emperor_gc_frame_head = co->gc_frames;
    sched_switch_to_co(co);
    co->gc_frames = _emperor_gc_frame_head;
    _emperor_gc_frame_head = saved_main_frames;
    sched_current = NULL;
    sched_thread_set_watermark(NULL);
    tls_on_coroutine = 0;
    if (MT_ALD(sched_exit_requested)) return 3;
    if (co_state_get(co) == CO_FINISHED) return 0;
    if (co_state_get(co) == CO_FD_PARKED) return 2;
    if (co_state_get(co) == CO_YIELD) return 6;
    if (co_state_get(co) == CO_COND) return 7;
    if (co_state_get(co) == CO_RUNNABLE) return 8; /* woken inside its park window:
                                               the wake's push already queued
                                               it — owed nothing, file nowhere
                                               (re-filing here double-linked
                                               qnext and truncated the owner's
                                               run queue: the N=4 hang) */
    return 1; /* CO_PARKED: re-queue for the next delta round */
#else
    /* Sequential fallback: run the entry inline on this stack — the no-op
     * wait never parks, so the coroutine always finishes within this call
     * (fd waits block inline inside fd_park_current). */
    if (co->entry) {
        void (*entry)(void*) = co->entry;
        void* arg = co->entry_arg;
        co->entry = NULL;
        co->entry_arg = NULL;
        entry(arg);
    }
    co_state_set(co, CO_FINISHED);
    sched_current = NULL;
    return 0;
#endif
}

/* Entry shim for a Penguin __ICoroutineEntry object: dispatch through the
 * vtable (interface_id = the interface's simple name, slot 0 = __enter, the
 * only method). __enter is `fun __enter(mut this)` — void return, single
 * pointer argument — a stable C signature. */
static void co_entry_iface(void* obj) {
    typedef void (*EnterFn)(void*);
    EnterFn fn = (EnterFn)_emperor_vtable_lookup(obj, "__ICoroutineEntry", 0);
    if (!fn) {
        fprintf(stderr, "emperor sched: spawn object does not implement __ICoroutineEntry\n");
        abort();
    }
    fn(obj);
}

/* ---- Public runtime API (externs from core_builtin.penguin) ---- */

/* `async f(args)` spawn: the desugar builds a ctx object implementing
 * __ICoroutineEntry (fields = evaluated args + future) and calls this. */
void _emperor_co_spawn_entry(void* entry_obj) {
    EmperorCoroutine* co = co_create(co_entry_iface);
    co->entry_arg = entry_obj;
    /* This struct holds the ONLY reference until the entry consumes it —
     * an explicit root keeps it alive across the spawn-to-first-switch
     * window (the struct's scan regions are gone in the precise world). */
    _emperor_gc_add_root(&co->entry_arg);
}

static void mt_glock_init(void);
static void mt_spawn_distribute(EmperorCoroutine* co);

/* Pass 10 (confinement analysis) rewrote the site's spawn call to this
 * variant: identical, plus the stealable bit — and, when workers exist,
 * PLACEMENT: the fresh never-run coroutine goes straight onto a
 * round-robin worker queue (mid-delta spawns otherwise pile up on the
 * spawning thread; idle workers do not steal, so without spawn-time
 * distribution a parallel fan-out would run serially). */
void _emperor_co_spawn_entry_stealable(void* entry_obj) {
    /* No auto-inbox: co_create's default inbox_push would DOUBLE-queue the
     * handle (mt_spawn_distribute queues it again) — the self-cycle that
     * hung N=1 and corrupted the queues at N>1. */
    EmperorCoroutine* co = co_create_queued(co_entry_iface, 0);
    co->entry_arg = entry_obj;
    co->stealable = 1;
    _emperor_gc_add_root(&co->entry_arg);
    mt_spawn_distribute(co);
}

/* Initial-routine spawn: the entry is a plain `void ()` Penguin function
 * (the LLVM emitter passes the function symbol directly). */
static void co_entry_fn0(void* fn) {
    typedef void (*Fn)(void);
    ((Fn)fn)();
}

void _emperor_co_spawn_fn0(void* fn) {
    EmperorCoroutine* co = co_create(co_entry_fn0);
    co->entry_arg = fn;
}

/* wait — park the current coroutine for one delta round. */
void _emperor_co_wait(void) {
    EmperorCoroutine* self = sched_current;
    if (!self) {
        fprintf(stderr, "Uncaught runtime error: 'wait' outside a coroutine (code 100)\n");
        fflush(stdout);
        exit(1);
    }
    co_state_set(self, CO_PARKED);
    /* record the frozen stack extent for the quiescence fingerprint */
    char marker;
    self->sp_park = (char*)&marker;
    /* Same extent governs GC scanning: frames below the parked sp are dead,
     * and a conservative scan of them pins garbage forever (each initial on
     * its own stack no longer shares the sequential main-stack reuse that
     * used to hide earlier routines' stale slots). */
    _emperor_gc_scan_set_live(self->block, self->sp_park);
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
    co_switch_to_sched(self);
#else
    /* Sequential fallback: no real park — the entry runs inline to
     * completion. The clock and timer table now live in Penguin-land; jump
     * them to the earliest pending deadline via the compiled helper so
     * timer waits (`wait n tick` poll loops) still terminate; concurrency
     * semantics are not preserved. */
    __builtin___sim_time_travel();
    co_state_set(self, CO_RUNNING);
#endif
}

/* mt-scheduler parks (scheduler_mt.penguin policy). Same GC bookkeeping as
 * _co_wait (the parked stack extent must be recorded either way — allocation
 * on another coroutine can trigger a collection while this one is parked);
 * they differ only in the state left behind, which _co_switch_in reports so
 * the mt loop knows whether the coroutine is owed a delta (CO_PARKED), still
 * runnable in the current delta (CO_YIELD) or waiting for an explicit wake
 * (CO_COND). */
static void co_park_state(EmperorCoroutine* self, int state) {
    co_state_set(self, state);
    char marker;
    self->sp_park = (char*)&marker;
    _emperor_gc_scan_set_live(self->block, self->sp_park);
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
    co_switch_to_sched(self);
#else
    /* Sequential fallback: no real park and no external waker can exist —
     * resume immediately (the mt loop's wake sources are other coroutines,
     * which cannot run here anyway). */
    co_state_set(self, CO_RUNNING);
#endif
}

void _emperor_co_yield(void) {
    EmperorCoroutine* self = sched_current;
    if (!self) {
        fprintf(stderr, "Uncaught runtime error: yield outside a coroutine (code 100)\n");
        fflush(stdout);
        exit(1);
    }
    co_park_state(self, CO_YIELD);
}

void _emperor_co_park(void) {
    EmperorCoroutine* self = sched_current;
    if (!self) {
        fprintf(stderr, "Uncaught runtime error: park outside a coroutine (code 100)\n");
        fflush(stdout);
        exit(1);
    }
    co_park_state(self, CO_COND);
}

/* Plain millisecond sleep for the mt scheduler's decision-E hang (parked
 * coroutines with no wake source: silent, cheap, indefinite). */
void _emperor_sched_msleep(int64_t ms) {
#if defined(EMPEROR_WIN_FIBERS)
    Sleep((DWORD)ms);
#else
    struct timespec ts;
    ts.tv_sec = ms / 1000;
    ts.tv_nsec = (ms % 1000) * 1000000L;
    nanosleep(&ts, NULL);
#endif
}

/* ---- External fd event sources ----
 * A coroutine may park on a file descriptor (readable or writable) instead of
 * the round queue. Such coroutines are external event sources: while any fd
 * waiter exists, quiescence no longer exits the program — the scheduler
 * blocks in poll() over the registered descriptors and the readiness of any
 * of them injects the next delta round. The POLICY (when to probe with
 * timeout 0, when to block with -1, what to do with the woken) is the
 * Penguin loop's; this file owns the waiter list and the syscall because
 * fd_park_current (running on the coroutine stack) must register itself
 * synchronously. */

typedef struct EmperorFdWaiter {
    struct EmperorFdWaiter* next;
    EmperorCoroutine* co;
    int fd;
    int for_write; /* 0 = wait readable, 1 = wait writable */
} EmperorFdWaiter;

static EmperorFdWaiter* fd_waiters = NULL;

/* Ready waiters staged for the Penguin loop (qnext-linked stack). */
static EmperorCoroutine* fd_woken_head = NULL;
static int64_t fd_woken_count = 0;

static void woken_push(EmperorCoroutine* co) {
    co->qnext = fd_woken_head;
    fd_woken_head = co;
    fd_woken_count++;
}

int64_t _emperor_fd_waiter_count(void) {
    int64_t n = 0;
    for (EmperorFdWaiter* w = fd_waiters; w; w = w->next) n++;
    return n;
}

void* _emperor_fd_take_woken(void) {
    EmperorCoroutine* co = fd_woken_head;
    if (!co) return NULL;
    fd_woken_head = co->qnext;
    fd_woken_count--;
    co->qnext = NULL;
    return co;
}

/* Park the current coroutine until the fd turns readable (for_write == 0) or
 * writable (for_write == 1). HUP/ERR conditions wake read waiters too — the
 * following read reports EOF, which the caller observes. */
static void fd_park_current(int fd, int for_write) {
    EmperorCoroutine* self = sched_current;
    if (!self) {
        fprintf(stderr,
                "Uncaught runtime error: fd wait outside a coroutine (code 100)\n");
        fflush(stdout);
        exit(1);
    }
    EmperorFdWaiter* w = (EmperorFdWaiter*)malloc(sizeof(EmperorFdWaiter));
    if (!w) {
        fprintf(stderr, "emperor sched: fd waiter allocation failed\n");
        exit(1);
    }
    w->co = self;
    w->fd = fd;
    w->for_write = for_write;
    w->next = fd_waiters;
    fd_waiters = w;
    co_state_set(self, CO_FD_PARKED);
    char marker;
    self->sp_park = (char*)&marker;
    _emperor_gc_scan_set_live(self->block, self->sp_park);
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
    co_switch_to_sched(self);
#else
    /* Sequential fallback: block inline (no other coroutine could run anyway)
     * and resume on this stack. */
    struct pollfd pfd;
    pfd.fd = fd;
    pfd.events = for_write ? (short)POLLOUT : (short)(POLLIN | POLLHUP | POLLERR);
    pfd.revents = 0;
    poll(&pfd, 1, -1);
    co_state_set(self, CO_RUNNING);
#endif
    /* The scheduler unlinks the waiter before re-queueing us, so there is
     * nothing to clean up here on either path. */
}

void _emperor_fd_wait_read(int64_t fd) { fd_park_current((int)fd, 0); }
void _emperor_fd_wait_write(int64_t fd) { fd_park_current((int)fd, 1); }

static void fd_unlink(EmperorFdWaiter* w) {
    EmperorFdWaiter** p = &fd_waiters;
    while (*p) {
        if (*p == w) {
            *p = w->next;
            free(w);
            return;
        }
        p = &(*p)->next;
    }
}

/* Poll every registered fd. Ready waiters are unlinked and STAGED (the
 * Penguin loop drains them with _fd_take_woken and re-queues the coroutines
 * itself). Returns how many were woken. timeout_ms < 0 blocks until an
 * event (or EINTR); 0 is a pure readiness probe. */
#ifdef EMPEROR_WIN_FIBERS
/* Level-triggered readiness probe of one registered descriptor.
 * Returns 1 ready, 0 not ready, -1 "indeterminate" — treated as ready: the
 * following read/write syscall reports the truth. A pipe whose write end
 * closed probes as a PeekNamedPipe failure, and the read then delivers the
 * EOF (empty) result, which is exactly the wake-on-HUP semantic the POSIX
 * poll branch gets from POLLHUP/POLLERR. */
static int win_fd_ready(int fd, int for_write) {
    HANDLE h = (HANDLE)_get_osfhandle(fd);
    if (h == INVALID_HANDLE_VALUE || h == NULL) return -1;
    if (for_write) {
        /* Anonymous pipes expose no writable-space query on Windows; the
         * write syscall blocks while the pipe is full, which preserves the
         * end-to-end backpressure the POLLOUT park provides on POSIX
         * (frames are written strictly in order by the single writer). */
        return 1;
    }
    switch (GetFileType(h)) {
    case FILE_TYPE_DISK:
        return 1; /* regular files are always readable */
    case FILE_TYPE_CHAR: {
        /* Console input buffer: queued input records = readable. */
        DWORD events = 0;
        if (GetNumberOfConsoleInputEvents(h, &events)) {
            return events > 0 ? 1 : 0;
        }
        return -1; /* a CHAR handle that is not a console: let the read report */
    }
    case FILE_TYPE_PIPE: {
        DWORD avail = 0;
        if (PeekNamedPipe(h, NULL, 0, NULL, &avail, NULL)) {
            return avail > 0 ? 1 : 0;
        }
        return -1; /* ERROR_BROKEN_PIPE et al.: the read delivers EOF/error */
    }
    default:
        return -1;
    }
}

int64_t _emperor_fd_poll(int64_t timeout_ms) {
    if (!fd_waiters) return 0;
    for (;;) {
        int woken = 0;
        for (EmperorFdWaiter* w = fd_waiters; w;) {
            EmperorFdWaiter* wnext = w->next; /* fd_unlink frees w */
            if (win_fd_ready(w->fd, w->for_write)) {
                EmperorCoroutine* co = w->co;
                fd_unlink(w);
                woken_push(co);
                woken++;
            }
            w = wnext;
        }
        if (woken) return woken;
        if (timeout_ms == 0) return 0; /* pure readiness probe */
        /* Block until an external event: there is no epoll-for-pipes on
         * Windows, so readiness is re-probed at a small interval (idle cost
         * is one PeekNamedPipe per waiter per 2 ms — the LSP parks exactly
         * one stdin reader while idle). */
        Sleep(2);
    }
}
#else
int64_t _emperor_fd_poll(int64_t timeout_ms) {
    if (!fd_waiters) return 0;
    size_t n = 0;
    for (EmperorFdWaiter* w = fd_waiters; w; w = w->next) n++;
    struct pollfd* pfds = (struct pollfd*)malloc(n * sizeof(struct pollfd));
    EmperorFdWaiter** order =
        (EmperorFdWaiter**)malloc(n * sizeof(EmperorFdWaiter*));
    if (!pfds || !order) {
        fprintf(stderr, "emperor sched: fd poll allocation failed\n");
        exit(1);
    }
    size_t i = 0;
    for (EmperorFdWaiter* w = fd_waiters; w; w = w->next) {
        order[i] = w;
        pfds[i].fd = w->fd;
        pfds[i].events =
            w->for_write ? (short)(POLLOUT) : (short)(POLLIN | POLLHUP | POLLERR);
        pfds[i].revents = 0;
        i++;
    }
    int rc = poll(pfds, (nfds_t)n, (int)timeout_ms);
    int64_t woken = 0;
    if (rc > 0) {
        for (i = 0; i < n; i++) {
            short re = pfds[i].revents;
            if (!re) continue;
            int wake = order[i]->for_write
                           ? (re & (POLLOUT | POLLERR | POLLHUP | POLLNVAL)) != 0
                           : (re & (POLLIN | POLLHUP | POLLERR | POLLNVAL)) != 0;
            if (!wake) continue;
            EmperorCoroutine* co = order[i]->co;
            fd_unlink(order[i]);
            woken_push(co);
            woken++;
        }
    }
    free(pfds);
    free(order);
    return woken; /* rc <= 0 (EINTR / error): the caller re-enters */
}
#endif /* fd poll variants */

/* exit(): from a coroutine, unwind to the scheduler and end the program with
 * the given code; from the main stack, exit immediately. */
void _emperor_sched_exit(int code) {
    if (sched_current) {
        MT_AST(sched_exit_requested, 1);
        sched_exit_code = code;
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
        co_switch_to_sched(sched_current);
#else
        exit(code);
#endif
        return;
    }
    exit(code);
}

int64_t _emperor_sched_exit_code(void) { return sched_exit_code; }

/* ---- try/catch (sjlj) ----
 * The compiled function itself calls _setjmp on a buffer from the per-site
 * table (LLVMEmitter inlines the call — a C-side wrapper's dead frame would
 * make the longjmp landing undefined: the landing reads a stack slot that
 * deeper calls have reused). _try_setup pushes {prev, jb} on the current
 * coroutine's try stack keyed by the site id; _try_leave pops+frees on the
 * normal path. Throw pops the frame, frees it and _longjmps to the saved
 * context — which resumes INSIDE the still-live compiled function.
 * A site's buffer is shared by recursive activations of that same site
 * (recursing through a try is not re-entrant in v1); distinct sites and
 * sequential re-entry (a try inside a loop) are safe. */
#define EMPEROR_TRY_SITES 1024
static jmp_buf _emperor_try_jb_table[EMPEROR_TRY_SITES];

/* Try stacks live per execution context: the current coroutine's try_top,
 * or — when user code runs directly on the main stack (sequential-mode
 * emit_main: a module with try/catch but no suspension points) — this
 * fallback stack. The two contexts never interleave user code (the
 * scheduler owns coroutines; sequential mode never enters it), so a single
 * fallback is sound. */
static EmperorTryFrame* _emperor_main_try_top = NULL;
static EmperorTryFrame** _emperor_try_top_slot(void) {
    return sched_current ? &sched_current->try_top : &_emperor_main_try_top;
}

void* _emperor_try_buf(int64_t site) {
    return (void*)&_emperor_try_jb_table[(size_t)site & (EMPEROR_TRY_SITES - 1)];
}

void _emperor_try_setup(int64_t site) {
    EmperorTryFrame** top = _emperor_try_top_slot();
    EmperorTryFrame* f = (EmperorTryFrame*)malloc(sizeof(EmperorTryFrame));
    if (!f) {
        fprintf(stderr, "emperor sched: try frame allocation failed\n");
        exit(1);
    }
    f->prev = *top;
    f->jb = (jmp_buf*)_emperor_try_buf(site);
    f->saved_gc_frames = _emperor_gc_frame_head;
    *top = f;
}

void _emperor_try_leave(void) {
    EmperorTryFrame** top = _emperor_try_top_slot();
    if (*top) {
        EmperorTryFrame* f = *top;
        *top = f->prev;
        free(f);
    }
}

/* Throw a runtime error: if the current coroutine has an enclosing try,
 * stash the payload and longjmp to the innermost handler; otherwise the
 * error is uncaught — report and exit non-zero (flushing buffered stdout
 * first, matching the BabyPenguin diagnostic shape). */
void _emperor_throw_runtime_error(_emperor_string* msg, int64_t code) {
    if (!_emperor_throw_msg_root_done) {
        /* Make the pending-throw message slot a GC root for this program —
         * the message must survive the longjmp window until the catch
         * handler reads it. Registered on first throw (any context: main
         * stack or coroutine) rather than at scheduler entry. */
        _emperor_gc_add_root((void**)&_emperor_throw_msg);
        _emperor_throw_msg_root_done = 1;
    }
    _emperor_throw_msg = msg;
    _emperor_throw_code = code;
    EmperorTryFrame** top = _emperor_try_top_slot();
    if (*top) {
        EmperorTryFrame* f = *top;
        *top = f->prev;
        /* Truncate the frame chain to the try-entry state BEFORE the longjmp:
         * every frame between the thrower and this try is abandoned without
         * running its descriptor unlink. */
        _emperor_gc_frame_head = f->saved_gc_frames;
        jmp_buf* jb = f->jb;
        free(f);
        _longjmp(*jb, 1);
    }
    fflush(stdout);
    fputs("Uncaught runtime error: ", stderr);
    if (msg) {
        fwrite(msg->data, 1, (size_t)msg->length, stderr);
    } else {
        fputs("?", stderr);
    }
    fprintf(stderr, " (code %lld)\n", (long long)code);
    _emperor_sched_exit(1);
}

_emperor_string* _emperor_throw_get_msg(void) { return _emperor_throw_msg; }
int64_t _emperor_throw_get_code(void) { return _emperor_throw_code; }

/* ---- Quiescence fingerprint (per coroutine) ----
 * Hash the coroutine's frozen stack ([parked sp, stack top]) plus the saved
 * sp. The Penguin loop combines the per-coroutine hashes over its all-live
 * registry (adoption order) and compares the combined value across two
 * consecutive idle rounds — only round-to-round equality matters, so the
 * combine rule is Penguin-side and need not match any C-side predecessor. */
static uint64_t fp_hash_bytes(uint64_t h, const char* p, size_t n) {
    while (n >= 8) {
        uint64_t v;
        memcpy(&v, p, 8);
        h = (h ^ v) * 0x9E3779B97F4A7C15ULL;
        p += 8;
        n -= 8;
    }
    if (n) {
        uint64_t v = 0;
        memcpy(&v, p, n);
        h = (h ^ v) * 0x9E3779B97F4A7C15ULL;
    }
    return h;
}

uint64_t _emperor_co_fingerprint(void* handle) {
    EmperorCoroutine* co = (EmperorCoroutine*)handle;
    uint64_t h = 0x9E3779B97F4A7C15ULL;
    h = fp_hash_bytes(h, (char*)&co->sp_park, sizeof(co->sp_park));
    const char* lo = co->sp_park ? co->sp_park : co->block;
    /* A fiber coroutine that never ran has no stack region yet (bounds
     * are discovered at first switch): its contribution is constant,
     * like a never-touched mmap page on POSIX. */
    if (co->stack_hi && lo && lo < co->stack_hi) {
        h = fp_hash_bytes(h, lo, (size_t)(co->stack_hi - lo));
    }
    return h;
}

/* ---- Entry point ----
 * The delta-round scheduler loop is Penguin code (__builtin.__sched_run in
 * std/penguin/scheduler.penguin, auto-loaded only with --enable-coroutine);
 * the emitter's emit_main calls the compiled function DIRECTLY
 * (@__builtin___sched_run — always defined in a suspending module's .ll).
 * No C trampoline exists on purpose: this object must stay free of Penguin
 * symbol references so flag-off programs (which never compile
 * scheduler.penguin) can still pull it from the archive — they do, for the
 * GC switch globals, try/catch sjlj and exit — and link cleanly. */

/* ---- MT execution kernel (M4a + M4b) ----
 * Owns the delta EXECUTION phase for scheduler_mt.penguin: run queues,
 * park filing, wake routing, the timer table and the future-waiter set.
 * The POLICY (the boundary's seven steps) stays in __sched_run: it calls
 * _emperor_mt_run_delta() once per delta and inspects the counters.
 *
 * M4b: EMPEROR_THREADS=N (env, default 1) starts N-1 worker threads; a
 * delta runs in parallel on all N (each thread drains its own queue;
 * Pass-10-stealable never-run coroutines are distributed round-robin at
 * delta start and stealable by idle threads). The delta ends at a global
 * barrier: every thread simultaneously out of work (single global lock —
 * the design doc's "第一版互斥锁版队列"). Decision A (started coroutines
 * never migrate) is what makes wake routing race-free: a wake goes to the
 * coroutine's owner, which by definition is not executing it.
 *
 * GC interop: collections trigger only from the boundary (_emperor_gc_poll
 * defers while _emperor_sched_parallel_active()); the allocator funnels
 * through gc.c's lock. With EMPEROR_THREADS unset the single worker is
 * the calling thread and behavior is byte-identical to M4a. */

/* The single scheduling lock (v0: one lock guards every queue, the owed
 * list, the future set, the timer table, idle bookkeeping and the delta
 * state — coarse by design; per-queue locks come later). */
static mt_mutex mt_glock;
static mt_cond mt_all_parked_cv;
static int mt_glock_ready = 0;
static int mt_nthreads = 1;        /* main (+ workers) */
static int mt_workers_started = 0;
static int mt_delta_running = 0; /* MT_ALD/MT_AST only */   /* a delta is in flight (parallel window) */
/* Coroutines popped-and-currently-executing (or popped-and-about-to-run):
 * invisible to every queue-head probe. Counted at POP time — inside the
 * pop's glock critical section, closing the pop-to-run gap — and dropped
 * when run_one returns (the coroutine has filed itself by park status).
 * The ESL settle probe reads this via _emperor_mt_inflight: a false 0
 * here let commit_all race a module evaluate (the N=8 failure). */
static int mt_inflight = 0;
static int mt_idle_count = 0;      /* parked inside idle_wait (delta in flight) */
static int mt_between_count = 0;   /* parked in worker_main's between-delta
                                      wait — contributes to CONVERGENCE only
                                      (see EmperorThreadInfo.between_parked) */
static int mt_rr = 0;              /* round-robin distributor cursor */

/* Delta-owed (bare `wait;`) parks, future waiters, timers. */
static EmperorCoroutine* mt_owed_head = NULL;
static EmperorCoroutine* mt_owed_tail = NULL;
static EmperorCoroutine* mt_futures_head = NULL;
static int64_t mt_futures_count = 0;
typedef struct MtTimer {
    struct MtTimer* next;
    int64_t deadline;
    EmperorCoroutine* co;
} MtTimer;
static MtTimer* mt_timers = NULL;
static int mt_stop = 0; /* MT_ALD/MT_AST only */
static int mt_dbg = 0;

/* M5 v0 pressure valve (spec §10.1): coroutines that touched the GC
 * allocation hard cap park here (CO_COND via mt_park_begin — NOT
 * runnable; their parking is part of what converges the delta). The
 * boundary, already the stop-the-world point, drains the forced
 * collection and then releases them into the next delta. qnext links the
 * list, exactly like the future-waiter set. The MINIMUM pending charge
 * feeds the boundary's OOM predicate (gc.c _emperor_gc_cap_blocks): when
 * even the smallest parked allocation cannot fit the live set, no
 * collection can ever unblock anyone. */
static EmperorCoroutine* mt_gc_parked_head = NULL;
static int64_t mt_gc_parked_count = 0;
static size_t mt_gc_parked_min_charge = (size_t)-1; /* SIZE_MAX = none */
/* Set by any mt-kernel entry (mt_glock_init): the valve parks only under
 * the mt scheduler — the classic scheduler's CO_PARKED has different
 * wake semantics (auto re-queue next round) and its inline collections
 * already bound growth. */
static int mt_kernel_seen = 0;

/* Parse a non-negative stall-diagnostic budget knob; a negative or zero
 * value disables the diagnostic. Unset or unparseable falls back to dflt.
 * (Statics above, next to mt_live_count.) */
static int64_t mt_env_budget(const char* name, int64_t dflt) {
    const char* v = getenv(name);
    if (!v || !*v) return dflt;
    char* end = NULL;
    long long n = strtoll(v, &end, 10);
    if (end == v) return dflt;
    return n < 0 ? 0 : (int64_t)n;
}

/* Report one stall class and stop the program with `code` (60 = a delta
 * that never converged, 61 = a clock pinned by zero-time churn). Called
 * from worker threads (a) and the boundary (b) — the CAS keeps the
 * message single; mt_stop folds every thread out of the delta exactly
 * like a coroutine exit() does. */
static void mt_stall_abort(int code, const char* what, const char* hint) {
    if (__atomic_compare_exchange_n(&mt_stall_reported, &(int){0}, 1, 0,
                                    __ATOMIC_SEQ_CST, __ATOMIC_SEQ_CST)) {
        fflush(stdout);
        fprintf(stderr, "emperor sched: stall (exit %d): %s\n", code, what);
        fprintf(stderr, "  %s; %lld coroutine(s) live. Raise the matching EMPEROR_SCHED_*_BUDGET knob to lengthen the leash, or set it to 0 to disable.\n",
                hint, (long long)mt_live_count);
        fflush(stderr);
    }
    sched_exit_code = code;
    MT_AST(mt_stop, 1);
}

static void mt_trace(const char* fmt, ...) {
    if (!__atomic_load_n(&mt_dbg, __ATOMIC_RELAXED)) return;
    va_list ap;
    va_start(ap, fmt);
    fprintf(stderr, "[mt t%d] ", tls_thread_index);
    vfprintf(stderr, fmt, ap);
    fputc('\n', stderr);
    va_end(ap);
}

/* Pre-futex spin (the N=8 convoy fix): consecutive deltas are microseconds
 * apart during a churn, so a worker that futex-parks between them burns two
 * syscalls and a context switch per delta — measured 2.7M futex + 1.1M
 * sigprocmask (swapcontext) calls in 15 s at EMPEROR_THREADS=8 (63% sys
 * time, ~8x per-tick wall time vs N=4). A bounded spin that EARLY-EXITS on
 * a relaxed view of the wake flags covers the gap with neither syscalls nor
 * added latency. The spin is never a decision: callers re-check under
 * mt_glock before acting, which keeps the wake protocol lossless (a push
 * lands either before the sleeper's re-acquire — visible in the predicate —
 * or after its cond_wait release, when the signal finds the sleeper; one
 * evaporated signal during the spin is covered by the locked re-check).
 * The shared-flag polls are BATCHED (32 pauses between reads): a poll per
 * pause iteration hammers the queue-head cache lines 8 spinners share with
 * every pusher — the cache-line ping-pong itself became the bottleneck
 * (clean runs decelerated while traced runs stayed steady). Latency stays
 * sub-µs; poll traffic drops ~30x. EMPEROR_MT_SPIN poll rounds, default
 * 4096 (~40 µs); 0 disables. */
static int64_t mt_spin_iters = -1; /* lazy env parse; -1 = unparsed */
static int64_t mt_spin_budget(void) {
    int64_t v = __atomic_load_n(&mt_spin_iters, __ATOMIC_RELAXED);
    if (v >= 0) return v;
    v = mt_env_budget("EMPEROR_MT_SPIN", 4096);
    int64_t expected = -1;
    __atomic_compare_exchange_n(&mt_spin_iters, &expected, v, 0,
                                __ATOMIC_RELAXED, __ATOMIC_RELAXED);
    return v; /* ours or a sibling's first parse — same env either way */
}
static void mt_pause(void) {
#if defined(__x86_64__) || defined(__i386__)
    __builtin_ia32_pause();
#elif defined(__aarch64__)
    __asm__ volatile("yield" ::: "memory");
#endif
}
#define MT_SPIN_BATCH 32

/* The registry array itself lives here now (M3 declared it earlier next to
 * the watermark helpers — see sched_threads/sched_thread_count there). */

static void mt_glock_init(void) {
    if (!mt_glock_ready) {
#if defined(EMPEROR_UCONTEXT) && defined(PTHREAD_MUTEX_ADAPTIVE_NP)
        /* N-thread convoy: the default mutex futex-parks on every loss, and
         * at EMPEROR_THREADS=8 the per-delta queue traffic (wake routing +
         * pops + steals + idle bookkeeping) collapses into a futex storm —
         * measured 63% sys time, ~8x per-tick wall time vs N=4. Spinning
         * first suits this lock: short critical sections, and worker cores
         * are otherwise idle (threads <= cores by construction). */
        pthread_mutexattr_t attr;
        pthread_mutexattr_init(&attr);
        pthread_mutexattr_settype(&attr, PTHREAD_MUTEX_ADAPTIVE_NP);
        pthread_mutex_init(&mt_glock, &attr);
        pthread_mutexattr_destroy(&attr);
#else
        MT_MUTEX_INIT(mt_glock);
#endif
        MT_COND_INIT(mt_all_parked_cv);
        /* Main's slot (0 once registered): its cv is signaled by the
         * barrier — initialize it here so it is valid before any use. */
        for (int i = 0; i < EMPEROR_SCHED_MAX_THREADS; i++) {
            MT_COND_INIT(sched_threads[i].cv);
        }
        mt_glock_ready = 1;
    }
    __atomic_store_n(&mt_kernel_seen, 1, __ATOMIC_RELAXED); /* every mt-kernel entry funnels through here */
}

static void mt_scan_lost_parked(void); /* defined after run_delta */

/* Queued-flag audit ring (debug): every `queued` transition is recorded
 * with its site; dumps replay the last window so a flag stranded at 1 (or
 * cleared early) can be traced to the exact operation. Sites: 1 push-set,
 * 2 pop-clear, 3 take-clear, 4 suppression-seen, 5 take-miss. */
#define MT_QLOG 8192
typedef struct MtQLogEntry {
    int seq;
    int old;
    int nw;
    int site;
    int tid;
    int state; /* co state at the transition */
} MtQLogEntry;
static MtQLogEntry mt_qlog[MT_QLOG];
static int64_t mt_qlog_total = 0;
static int mt_qlog_on = -1; /* -1 unparsed; EMPEROR_QLOG gates the ring */
static void mt_qlog_add(EmperorCoroutine* co, int old, int nw, int site) {
    if (__atomic_load_n(&mt_qlog_on, __ATOMIC_RELAXED) == 0) return;
    if (__atomic_load_n(&mt_qlog_on, __ATOMIC_RELAXED) < 0) {
        const char* v = getenv("EMPEROR_QLOG");
        __atomic_store_n(&mt_qlog_on, (v && v[0] != '\0' && v[0] != '0') ? 1 : 0,
                         __ATOMIC_RELAXED);
        if (__atomic_load_n(&mt_qlog_on, __ATOMIC_RELAXED) == 0) return;
    }
    int64_t i = __atomic_fetch_add(&mt_qlog_total, 1, __ATOMIC_RELAXED);
    MtQLogEntry* e = &mt_qlog[i & (MT_QLOG - 1)];
    e->seq = co->seq;
    e->old = old;
    e->nw = nw;
    e->site = site;
    e->tid = tls_thread_index;
    e->state = co_state_get(co);
}

/* Queue ops (mt_glock held by caller). */
static void mt_runq_push_locked(int slot, EmperorCoroutine* co) {
    if (co->queued) {
        /* A suppressed push means `queued` is set with a live queue entry.
         * For a parked CO_COND coroutine this is LEGAL and expected: an
         * earlier wake's phantom entry already files it, and a second
         * filer dropping its push strands nothing (the phantom IS the
         * wake). Any other combination is the kernel-invariant break the
         * qlog site 4 event flags for the replay. */
        mt_qlog_add(co, 1, 1, 4);
        mt_trace("double push suppressed co=%d(seq) state=%d", co->seq, co_state_get(co));
        return; /* double insert — see the struct's comment */
    }
    mt_qlog_add(co, 0, 1, 1);
    co->queued = 1;
    co->qnext = NULL;
    EmperorThreadInfo* t = &sched_threads[slot];
    if (MT_ALD(t->between_parked)) {
        /* Work landed on a between-delta sleeper: its parked bookkeeping
         * dies HERE (the sleeper's wake path clears idempotently), and the
         * under-lock signal cannot be lost — the sleeper cannot have left
         * cond_wait without this lock. Without this, a converged delta
         * could leave the queued coroutine unprocessed until a later open
         * (the lost-UART-char bug: the clock's stop hook saw quiescence
         * with a char delivery still parked). */
        MT_AST(t->between_parked, 0);
        MT_AFS(mt_between_count, 1);
        MT_SIGNAL(t->cv);
    }
    if (t->runq_tail) {
        t->runq_tail->qnext = co;
        t->runq_tail = co;
    } else {
        MT_AST(t->runq_head, co);
        t->runq_tail = co;
    }
}

static EmperorCoroutine* mt_runq_pop_locked(int slot) {
    EmperorThreadInfo* t = &sched_threads[slot];
    EmperorCoroutine* co = MT_ALD(t->runq_head);
    if (!co) return NULL;
    MT_AST(t->runq_head, co->qnext);
    if (!MT_ALD(t->runq_head)) t->runq_tail = NULL;
    co->qnext = NULL;
    mt_qlog_add(co, 1, 0, 2);
    co->queued = 0;
    return co;
}

/* Any queued coroutine anywhere (mt_glock held)? CONVERGENCE requires this
 * to be false alongside the parked count: a queued coroutine whose owner is
 * (or was) asleep would otherwise let the delta end with work unprocessed —
 * the invariant the all-awake protocol maintained implicitly (a queued
 * coroutine kept its owner looping, never idle) and the between-delta
 * sleeper protocol must now check explicitly. */
static int mt_any_runq_nonempty_locked(void) {
    for (int i = 0; i < sched_thread_count; i++) {
        if (sched_threads[i].used && MT_ALD(sched_threads[i].runq_head)) return 1;
    }
    return 0;
}

/* Unlink one specific entry (steal target) — walks with a prev pointer. */
static EmperorCoroutine* mt_runq_take_locked(int slot, EmperorCoroutine* target) {
    EmperorThreadInfo* t = &sched_threads[slot];
    EmperorCoroutine* prev = NULL;
    EmperorCoroutine* cur = MT_ALD(t->runq_head);
    while (cur) {
        if (cur == target) {
            if (prev) prev->qnext = cur->qnext;
            else MT_AST(t->runq_head, cur->qnext);
            if (t->runq_tail == target) {
                t->runq_tail = prev;
            }
            target->qnext = NULL;
            mt_qlog_add(target, 1, 0, 3);
            target->queued = 0;
            return target;
        }
        prev = cur;
        cur = cur->qnext;
    }
    return NULL;
}

/* _emperor_co_destroy's unfile guard (forward-declared near the top): a
 * coroutine woken inside its park window carries a run-queue entry while it
 * runs; if it then finishes, the entry must be unlinked before the stack is
 * munmap'd. The futures entry is defensive only — the no-consume protocol
 * unlinks it on every do_wait exit path — but a destroyed-and-still-linked
 * coroutine would be a use-after-free for every later kick. Under the
 * classic scheduler no run queues exist (mt_kernel_seen is 0) and this is a
 * no-op. */
static void mt_unfile_runq_phantom(EmperorCoroutine* co) {
    if (!__atomic_load_n(&mt_kernel_seen, __ATOMIC_RELAXED)) return;
    mt_glock_init();
    MT_LOCK(mt_glock);
    for (int i = 0; i < sched_thread_count; i++) {
        mt_runq_take_locked(i, co);
    }
    {
        EmperorCoroutine** pp = &mt_futures_head;
        while (*pp) {
            if (*pp == co) {
                *pp = co->fnext;
                mt_futures_count--;
                break;
            }
            pp = &(*pp)->fnext;
        }
        co->fnext = NULL;
        co->in_futures = 0;
    }
    MT_UNLOCK(mt_glock);
}

/* Spawn-time placement for stealable coroutines (decision: placement is
 * automatic; a mid-delta fan-out must not serialize on the spawner). */
static void mt_spawn_distribute(EmperorCoroutine* co) {
    mt_glock_init();
    MT_LOCK(mt_glock);
    if (MT_ALD(mt_delta_running) && mt_nthreads > 1) {
        mt_rr = (mt_rr + 1) % mt_nthreads;
        int slot = 0;
        int seen = 0;
        for (int i = 0; i < sched_thread_count; i++) {
            if (!sched_threads[i].used) continue;
            if (seen == mt_rr) { slot = i; break; }
            seen++;
        }
        mt_runq_push_locked(slot, co);
        int target = slot;
        MT_UNLOCK(mt_glock);
        if (MT_ALD(sched_threads[target].idle) || MT_ALD(sched_threads[target].between_parked)) {
            MT_SIGNAL(sched_threads[target].cv);
        }
        return;
    }
    /* Between deltas (or single-threaded): the normal spawn inbox — the
     * next delta's distributor round-robins it (or main runs it alone). */
    inbox_push(co);
    MT_UNLOCK(mt_glock);
}

static void mt_owed_push_locked(EmperorCoroutine* co) {
    if (co->on_owed || co->queued) {
        mt_qlog_add(co, co->on_owed ? 1 : 0, co->on_owed ? 1 : 0, 13); /* owed dup refused */
        return; /* already owed (or queued — a wake's phantom outran the
                   bare-wait filing): a second link would self-cycle the
                   tail and corrupt both lists (qnext is shared) */
    }
    mt_qlog_add(co, 0, 1, 14); /* owed push */
    mt_trace("owed_push co=%d state=%d queued=%d", co->seq,
             co_state_get(co), co->queued);
    co->on_owed = 1;
    co->onext = NULL;
    if (mt_owed_tail) {
        mt_owed_tail->onext = co;
        mt_owed_tail = co;
    } else {
        mt_owed_head = co;
        mt_owed_tail = co;
    }
}

/* Claim the parked state BEFORE waiter-list registration so no wake can be
 * lost between "registered" and "parked". GC bookkeeping happens here; the
 * switch itself is mt_switch_away — the caller registers in ITS waiter
 * list between the two. */
static void mt_park_begin(void) {
    EmperorCoroutine* self = sched_current;
    if (!self) {
        fprintf(stderr, "Uncaught runtime error: park outside a coroutine (code 100)\n");
        fflush(stdout);
        exit(1);
    }
    self->lib_park = 1;
    co_state_set(self, CO_COND);
    char marker;
    self->sp_park = (char*)&marker;
    _emperor_gc_scan_set_live(self->block, self->sp_park);
}

static void mt_switch_away(void) {
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
    EmperorCoroutine* self = sched_current;
    /* Claim CO_COND AT the switch. The register-time claim can be clobbered
     * by a yield backoff inside the registered poll window (_sync_begin's
     * _co_yield parks CO_YIELD; the resumed code continues with state
     * RUNNING from its switch-in): parking with the stale state fell
     * through run_one's status ladder into the bare-wait owed filing — a
     * wait the coroutine never made, re-queued every delta at the same
     * tick, pinning the virtual clock forever (the N>=5 steal x ESL
     * freeze). Every caller of this switch is a CO_COND protocol (future
     * wait, Fifo backpressure, the GC valve), so the claim here is both
     * correct and atomic with the park itself. */
    co_state_set(self, CO_COND);
    /* Last-resort filing: a CO_COND park must be findable by SOME waker.
     * Under the no-consume futures protocol this cannot fire for a future
     * park (the entry stays linked for the whole do_wait); it remains as
     * the invariant net for a park whose every filing was spent against
     * the protocol's expectations. If BOTH the futures entry and a phantom
     * queue entry are absent, park as a one-delta owed request instead:
     * the resume re-runs the wait loop's register, so the rescue is
     * bounded — unlike the historical fallthrough misfiling it cannot pin
     * the tick forever. */
    {
        mt_glock_init();
        MT_LOCK(mt_glock);
        if (self->future_park && !self->in_futures && !self->queued) {
            mt_qlog_add(self, 0, 0, 12); /* owed rescue */
            mt_owed_push_locked(self);
        }
        MT_UNLOCK(mt_glock);
    }
    /* Record the frozen extent HERE, not at the state claim: the Mesa-
     * ordered future wait runs poll/unregister frames between claiming
     * CO_COND and this switch, and those frames must stay inside the
     * scanned range (a re-record here is shallower than park_begin's —
     * the conservative direction). */
    char marker;
    self->sp_park = (char*)&marker;
    _emperor_gc_scan_set_live(self->block, self->sp_park);
    co_switch_to_sched(self);
#else
    co_state_set(sched_current, CO_RUNNING);
#endif
}

/* The wake's core (mt_glock held): CO_COND -> CO_RUNNABLE + owner-queue
 * push. Extracted so _emperor_mt_kick_futures can fan out to every parked
 * waiter under one acquisition. A suppressed push here is LEGAL: a parked
 * CO_COND entry can still carry an earlier wake's phantom queue entry
 * (queued=1) — that entry IS this wake's filing. */
static void mt_wake_locked(EmperorCoroutine* co) {
    mt_qlog_add(co, 0, 0, 10); /* wake took effect */
    co_state_set(co, CO_RUNNABLE);
    int slot = co_owner_get(co);
    if (slot < 0 || slot >= sched_thread_count || !sched_threads[slot].used) {
        slot = 0; /* pre-first-run fallback: main's queue */
    }
    mt_runq_push_locked(slot, co);
    if (MT_ALD(sched_threads[slot].idle) || MT_ALD(sched_threads[slot].between_parked)) {
        MT_SIGNAL(sched_threads[slot].cv);
    }
}

/* Route one wake. The state check and the queue push are ONE critical
 * section: a coroutine woken between its future REGISTER and its
 * switch_away gets a run-queue entry while still executing (a "phantom"),
 * and the runner-side cancel (_emperor_mt_future_cancel) must be able to
 * decide — under this same lock — whether a phantom exists (state ==
 * CO_RUNNABLE) and unlink it. The target is the coroutine's OWNER queue —
 * decision A guarantees the owner is not executing it, so the push can
 * never race its own run. */
void _emperor_mt_wake(void* handle) {
    EmperorCoroutine* co = (EmperorCoroutine*)handle;
    mt_glock_init();
    MT_LOCK(mt_glock);
    if (co_state_get(co) == CO_COND) {
        mt_wake_locked(co);
        MT_UNLOCK(mt_glock);
        return;
    }
    mt_qlog_add(co, 0, 0, 11); /* wake no-op (state not CO_COND) */
    MT_UNLOCK(mt_glock);
}

/* The IFuture.do_wait park, Mesa-ordered (the poll-then-register order lost
 * exactly the wake that ran between the two — the N=4 silent hang's second
 * half): _mt_future_register CLAIMS the parked state and the future-waiter
 * slot WITHOUT parking. The poll the caller runs after the claim observes
 * any completion whose kick already ran (its state change is durable); a
 * completion after the claim finds the waiter registered and kicks it.
 * _mt_switch_away parks (recording the frozen stack extent there); the
 * entry leaves the futures list ONLY on the waiter's own exit paths
 * (unregister/cancel) — kicks never unlink it (no-consume), so the flag
 * cannot go stale and a parked waiter is always findable. */
void _emperor_mt_future_register(void) {
    EmperorCoroutine* self = sched_current;
    self->future_park = 1;
    co_state_set(self, CO_COND); /* claim: every wake from here on re-queues us */
    mt_glock_init();
    MT_LOCK(mt_glock);
    if (self->in_futures) {
        MT_UNLOCK(mt_glock);
        return; /* invariant guard: with the no-consume protocol the flag
                   is set ONLY while genuinely linked (unregister/cancel/
                   destroy clear it under this lock), so a set flag means
                   this do_wait already filed itself — linking again would
                   double-link and orphan the exit bookkeeping */
    }
    mt_qlog_add(self, 0, 0, 6); /* futures add */
    self->in_futures = 1;
    self->fnext = mt_futures_head;
    mt_futures_head = self;
    mt_futures_count++;
    MT_UNLOCK(mt_glock);
}

void _emperor_mt_future_unregister(void) {
    EmperorCoroutine* self = sched_current;
    MT_LOCK(mt_glock);
    EmperorCoroutine** pp = &mt_futures_head;
    while (*pp) {
        if (*pp == self) {
            mt_qlog_add(self, 0, 0, 7); /* futures remove */
            *pp = self->fnext;
            mt_futures_count--;
            self->in_futures = 0;
            break;
        }
        pp = &(*pp)->fnext;
    }
    self->fnext = NULL;
    self->future_park = 0;
    MT_UNLOCK(mt_glock);
}

/* The runner-side half of the Mesa handshake: the do_wait that registered
 * and then found the future READY (or hit a throw) without ever parking.
 * If a kick won the register→poll race, state is CO_RUNNABLE and a phantom
 * run-queue entry points at this still-executing coroutine — unlink it and
 * reclaim RUNNING, all under mt_glock so a concurrent wake cannot slip a
 * push in between the state check and the unlink (wake's check+push is the
 * same critical section). Also unfiles the futures entry (idempotent). */
void _emperor_mt_future_cancel(void) {
    EmperorCoroutine* self = sched_current;
    mt_glock_init();
    MT_LOCK(mt_glock);
    if (co_state_get(self) == CO_RUNNABLE) {
        for (int i = 0; i < sched_thread_count; i++) {
            mt_runq_take_locked(i, self);
        }
    }
    co_state_set(self, CO_RUNNING);
    EmperorCoroutine** pp = &mt_futures_head;
    while (*pp) {
        if (*pp == self) {
            mt_qlog_add(self, 0, 0, 7); /* futures remove (cancel) */
            *pp = self->fnext;
            mt_futures_count--;
            self->in_futures = 0;
            break;
        }
        pp = &(*pp)->fnext;
    }
    self->fnext = NULL;
    self->future_park = 0;
    MT_UNLOCK(mt_glock);
}

/* Wake every parked future waiter (any delivery, any thread — wake-all
 * Mesa semantics: every waiter re-polls its own future and re-parks or
 * proceeds). NO-CONSUME protocol: the futures entry is the waiter's park
 * filing for the WHOLE do_wait, not a one-shot token — the kick only
 * flips CO_COND entries to runnable (a queue push); the entry stays
 * linked until the waiter itself unregisters/cancels on exit. The old
 * consume-and-re-register handshake had an unfixable seam: any path that
 * unlinked the entry without the runner's knowledge left in_futures set
 * with the coroutine parked NOWHERE (the ~16-cycle self-heal churn, and —
 * when the repair landed later than the pipeline depth — a module
 * evaluating a stale edge and the lost-UART-char mis-execution). With the
 * entry permanent, a parked registered waiter is findable BY
 * CONSTRUCTION; state == CO_COND is the "needs a wake" epoch marker. */
void _emperor_mt_kick_futures(void) {
    mt_glock_init();
    MT_LOCK(mt_glock);
    int64_t kicked = 0;
    for (EmperorCoroutine* co = mt_futures_head; co; co = co->fnext) {
        if (co_state_get(co) == CO_COND) {
            mt_qlog_add(co, 0, 0, 8); /* kick wakes (entry stays linked) */
            mt_wake_locked(co);
            kicked++;
        }
    }
    MT_UNLOCK(mt_glock);
    if (kicked > 0) {
        mt_trace("kick_futures n=%lld", (long long)kicked);
    }
}

/* gc.c's pressure valve (§10.1): the current coroutine touched the
 * allocation hard cap — park it on "wait for GC" and return 1 after the
 * boundary's post-collection unpark resumed it. `charge` is the parked
 * allocation's byte cost (feeds the boundary's min-charge OOM check).
 * Returns 0 when no coroutine can park here (main stack, the classic
 * scheduler, the sequential fallback); gc.c then allocates anyway,
 * leaving the goal cadence untouched. The state is CO_COND (owed
 * nothing) but the ONLY waker is the boundary's unpark walk — an
 * allocation-blocked coroutine is by definition not runnable, which is
 * what lets an all-blocked delta converge into the boundary's
 * stop-the-world collection. */
int _emperor_sched_alloc_park(size_t charge) {
    if (!__atomic_load_n(&mt_kernel_seen, __ATOMIC_RELAXED)) return 0;
    if (tls_sync_depth > 0) return 0; /* holding a component lock: parking
                                         here would sleep on the spinlock */
    EmperorCoroutine* self = sched_current;
    if (!self) return 0;
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
    mt_park_begin();
    mt_glock_init();
    MT_LOCK(mt_glock);
    self->qnext = mt_gc_parked_head;
    mt_gc_parked_head = self;
    mt_gc_parked_count++;
    if (charge < mt_gc_parked_min_charge) {
        mt_gc_parked_min_charge = charge;
    }
    MT_UNLOCK(mt_glock);
    mt_switch_away();
    /* Resumed: the boundary's unpark already unlinked us (mt_gc_parked
     * is empty of us — nothing to clean up). */
    return 1;
#else
    return 0; /* sequential fallback: mt_switch_away cannot really park */
#endif
}

/* The poll-side STW escalation (free-mt / long-delta GC): a goal-raised
 * want_collect that the boundary cannot drain (the delta never converges —
 * a busy service) escalates after EMPEROR_GC_STW_MS: the polling coroutine
 * files onto the same parked list as the hard-cap valve, which forces
 * convergence (parked coroutines are not runnable), the boundary collects
 * at goal-sized heaps instead of cap-sized, and the unpark resumes here.
 * Charge is 0: no OOM predicate applies to an escalation parker. Returns 1
 * if it parked (and has been resumed). */
int _emperor_sched_stw_park(void) {
    if (!__atomic_load_n(&mt_kernel_seen, __ATOMIC_RELAXED)) return 0;
    if (tls_sync_depth > 0) return 0; /* holding a component lock */
    EmperorCoroutine* self = sched_current;
    if (!self) return 0;
    if (!(mt_nthreads > 1 && MT_ALD(mt_delta_running))) return 0;
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
    mt_park_begin();
    mt_glock_init();
    MT_LOCK(mt_glock);
    self->qnext = mt_gc_parked_head;
    mt_gc_parked_head = self;
    mt_gc_parked_count++;
    MT_UNLOCK(mt_glock);
    mt_switch_away();
    return 1;
#else
    return 0;
#endif
}

void* _emperor_co_current(void) {
    return sched_current;
}

/* Atomic id source for library counters that coroutines on different
 * threads might bump concurrently (Event subscription ids). */
int64_t _emperor_mt_next_id(void) {
    static int64_t mt_id_counter = 0;
    return __atomic_add_fetch(&mt_id_counter, 1, __ATOMIC_SEQ_CST);
}

void _emperor_mt_timer_at(int64_t deadline) {
    EmperorCoroutine* self = sched_current;
    if (!self) return;
    MtTimer* t = (MtTimer*)malloc(sizeof(MtTimer));
    if (!t) {
        fprintf(stderr, "emperor sched: timer entry allocation failed\n");
        exit(1);
    }
    t->deadline = deadline;
    t->co = self;
    mt_glock_init();
    MT_LOCK(mt_glock);
    t->next = mt_timers;
    mt_timers = t;
    MT_UNLOCK(mt_glock);
}

int64_t _emperor_mt_timer_earliest(void) {
    MT_LOCK(mt_glock);
    int64_t best = -1;
    for (MtTimer* t = mt_timers; t; t = t->next) {
        if (best < 0 || t->deadline < best) best = t->deadline;
    }
    MT_UNLOCK(mt_glock);
    return best;
}

int64_t _emperor_mt_fire_due(int64_t now) {
    MT_LOCK(mt_glock);
    MtTimer* due_head = NULL;
    MtTimer** pp = &mt_timers;
    int64_t fired = 0;
    while (*pp) {
        if ((*pp)->deadline <= now) {
            MtTimer* t = *pp;
            *pp = t->next;
            t->next = due_head;
            due_head = t;
            fired++;
        } else {
            pp = &(*pp)->next;
        }
    }
    MT_UNLOCK(mt_glock);
    if (fired > 0) mt_trace("fire_due now=%lld fired=%lld", (long long)now,
                            (long long)fired);
    while (due_head) {
        MtTimer* t = due_head;
        due_head = t->next;
        /* Stall diagnostic (b): a fire at a STRICTLY later deadline than
         * anything fired before is real clock progress — it resets the
         * pin streak. Refires at an already-seen deadline (a `wait 0`
         * spin re-registering the same deadline every delta) are zero-time
         * churn and deliberately do NOT reset it. */
        if (t->deadline > mt_pin_max_deadline) {
            mt_pin_streak = 0;
            mt_pin_max_deadline = t->deadline;
        }
        _emperor_mt_wake(t->co);
        free(t);
    }
    return fired;
}

static void mt_timers_purge_co_locked(EmperorCoroutine* co) {
    MtTimer** pp = &mt_timers;
    while (*pp) {
        if ((*pp)->co == co) {
            MtTimer* t = *pp;
            *pp = t->next;
            free(t);
        } else {
            pp = &(*pp)->next;
        }
    }
}

int64_t _emperor_mt_owed_count(void) {
    MT_LOCK(mt_glock);
    int64_t n = 0;
    for (EmperorCoroutine* c = mt_owed_head; c; c = c->onext) n++;
    MT_UNLOCK(mt_glock);
    return n;
}

void _emperor_mt_requeue_owed(void) {
    MT_LOCK(mt_glock);
    int64_t n = 0;
    while (mt_owed_head) {
        EmperorCoroutine* co = mt_owed_head;
        mt_owed_head = co->onext;
        co->onext = NULL;
        co->on_owed = 0;
        int slot = co_owner_get(co);
        if (slot < 0 || !sched_threads[slot].used) slot = 0;
        mt_qlog_add(co, 0, 0, 15); /* owed requeue */
        mt_trace("requeue_owed co=%d state=%d queued=%d -> t%d", co->seq,
                 co_state_get(co), co->queued, slot);
        mt_runq_push_locked(slot, co);
        n++;
    }
    mt_owed_tail = NULL;
    MT_UNLOCK(mt_glock);
    if (n > 0) mt_trace("requeue_owed n=%lld", (long long)n);
}

int64_t _emperor_mt_inflight(void) {
    return __atomic_load_n(&mt_inflight, __ATOMIC_RELAXED);
}

/* Steps executed by the JUST-FINISHED delta (the Penguin boundary's churn
 * leash reads this at its top: mt_step_count resets only at the NEXT
 * _mt_run_delta, so the value survives until then). */
int64_t _emperor_mt_delta_steps(void) {
    return mt_step_count;
}

/* Monotonic wall clock in ms (the churn leash's stall window — a frozen or
 * ultra-slow tick with live coroutines is the freeze class, and a per-tick
 * round counter alone misses the slow variant: an occasional timer fire
 * resets the streak while the sim still crawls). */
int64_t _emperor_mt_now_ms(void) {
#if defined(EMPEROR_WIN_FIBERS)
    return (int64_t)GetTickCount64();
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
#endif
}

int64_t _emperor_mt_runnable(void) {
    MT_LOCK(mt_glock);
    int any = 0;
    for (int i = 0; i < sched_thread_count; i++) {
        if (sched_threads[i].used && MT_ALD(sched_threads[i].runq_head)) { any = 1; break; }
    }
    MT_UNLOCK(mt_glock);
    return any;
}

int64_t _emperor_mt_live_count(void) {
    return mt_live_count;
}

/* Policy-side _sched_sync gating: at one thread the spinlock can never
 * contend, and the swap+yield retry burns a full coroutine round trip per
 * hold — skip it (the mt kernel is the only nthreads source; the value is
 * fixed after workers start). */
int64_t _emperor_mt_nthreads(void) {
    return mt_nthreads;
}

/* ---- Decision-E idle diagnostics (lost-wake reporter) ----
 * A converged delta that ran nothing, with no owed parkers, no timers and
 * no fd waiters, while coroutines are still live, is the decision-E hang:
 * some coroutine is parked and nobody will ever wake it. EMPEROR_IDLE_DUMP=N
 * (default 1000 ≈ 10 s of idle rounds; 0 disables) turns that silent hang
 * into a report: every live coroutine's park filing is dumped to stderr and
 * the program stops with exit 63. The interesting line is a CO_COND
 * coroutine parked NOWHERE — not in any run queue, the owed list, the
 * future set, the timer table, the GC valve or the fd waiters: its wake
 * was lost before its park registered (or its Penguin-side library waiter
 * list lost it — the C-side filing cannot see those). */
static const char* mt_co_state_name(int s) {
    switch (s) {
    case CO_READY: return "READY";
    case CO_RUNNING: return "RUNNING";
    case CO_PARKED: return "PARKED";
    case CO_FINISHED: return "FINISHED";
    case CO_FD_PARKED: return "FD_PARKED";
    case CO_YIELD: return "YIELD";
    case CO_COND: return "COND";
    case CO_RUNNABLE: return "RUNNABLE";
    default: return "?";
    }
}

void _emperor_mt_dump_parked(const char* reason) {
    fflush(stdout);
    fprintf(stderr,
            "[idle-dump] %s: live=%lld threads=%d delta_running=%d stop=%d idle=%d between=%d\n",
            reason, (long long)mt_live_count, mt_nthreads, MT_ALD(mt_delta_running),
            MT_ALD(mt_stop), MT_ALD(mt_idle_count), MT_ALD(mt_between_count));
    MT_LOCK(mt_glock);
    for (int i = 0; i < sched_thread_count; i++) {
        if (!sched_threads[i].used) continue;
        int n = 0;
        for (EmperorCoroutine* c = MT_ALD(sched_threads[i].runq_head); c; c = c->qnext) n++;
        fprintf(stderr, "[idle-dump] t%d idle=%d between=%d runq=%d loop_iters=%lld\n", i,
                MT_ALD(sched_threads[i].idle), MT_ALD(sched_threads[i].between_parked), n,
                (long long)MT_ALD(sched_threads[i].loop_iters));
    }
    int64_t owed = 0;
    for (EmperorCoroutine* c = mt_owed_head; c; c = c->onext) owed++;
    fprintf(stderr, "[idle-dump] owed=%lld futures=%lld gc_parked=%lld fd_waiters=%lld\n",
            (long long)owed, (long long)mt_futures_count,
            (long long)mt_gc_parked_count, (long long)_emperor_fd_waiter_count());
    {
        fprintf(stderr, "[idle-dump] futures chain:");
        int64_t n = 0;
        for (EmperorCoroutine* c = mt_futures_head; c; c = c->fnext) {
            fprintf(stderr, " co%d(f=%d)", c->seq, c->in_futures);
            n++;
            if (n > 16) { fprintf(stderr, " ..."); break; }
        }
        fprintf(stderr, " (count walked %lld, book %lld)\n", (long long)n,
                (long long)mt_futures_count);
        fprintf(stderr, "[idle-dump] owed chain:");
        for (EmperorCoroutine* c = mt_owed_head; c; c = c->onext) {
            fprintf(stderr, " co%d", c->seq);
        }
        fprintf(stderr, "\n");
    }
    for (MtTimer* t = mt_timers; t; t = t->next) {
        fprintf(stderr, "[idle-dump] timer co=%d deadline=%lld\n", t->co->seq,
                (long long)t->deadline);
    }
    for (EmperorCoroutine* co = co_all_head; co; co = co->all_next) {
        char where[96];
        int found = 0;
        for (int i = 0; i < sched_thread_count && !found; i++) {
            if (!sched_threads[i].used) continue;
            for (EmperorCoroutine* c = MT_ALD(sched_threads[i].runq_head); c; c = c->qnext) {
                if (c == co) {
                    snprintf(where, sizeof where, "runq[t%d]", i);
                    found = 1;
                    break;
                }
            }
        }
        if (!found) {
            if (co->on_owed) { snprintf(where, sizeof where, "owed"); found = 1; }
        }
        if (!found) {
            for (EmperorCoroutine* c = mt_futures_head; c; c = c->fnext) {
                if (c == co) { snprintf(where, sizeof where, "futures"); found = 1; break; }
            }
        }
        if (!found) {
            for (MtTimer* t = mt_timers; t; t = t->next) {
                if (t->co == co) {
                    snprintf(where, sizeof where, "timer(d=%lld)", (long long)t->deadline);
                    found = 1;
                    break;
                }
            }
        }
        if (!found) {
            for (EmperorCoroutine* c = mt_gc_parked_head; c; c = c->qnext) {
                if (c == co) { snprintf(where, sizeof where, "gc_valve"); found = 1; break; }
            }
        }
        if (!found) {
            for (EmperorFdWaiter* w = fd_waiters; w; w = w->next) {
                if (w->co == co) {
                    snprintf(where, sizeof where, "fd(%d)%s", w->fd,
                             w->for_write ? "w" : "r");
                    found = 1;
                    break;
                }
            }
        }
        if (!found) {
            snprintf(where, sizeof where, "%s",
                     co_state_get(co) == CO_COND ? "NOWHERE(lost wake?)" : "nowhere");
        }
        fprintf(stderr, "[idle-dump] co=%d state=%s owner=%d queued=%d steal=%d at=%s\n",
                co->seq, mt_co_state_name(co_state_get(co)), co_owner_get(co), co->queued,
                co->stealable, where);
    }
    MT_UNLOCK(mt_glock);
    /* Queued-flag audit tail: the last MT_QLOG transitions, oldest first
     * (sites: 1 push-set, 2 pop-clear, 3 take-clear, 4 suppressed-push). */
    {
        int64_t total = __atomic_load_n(&mt_qlog_total, __ATOMIC_RELAXED);
        int64_t start = total > MT_QLOG ? total - MT_QLOG : 0;
        fprintf(stderr, "[qlog] total=%lld, replaying %lld entries\n",
                (long long)total, (long long)(total - start));
        for (int64_t i = start; i < total; i++) {
            MtQLogEntry* e = &mt_qlog[i & (MT_QLOG - 1)];
            fprintf(stderr,
                    "[qlog] #%lld co=%d %d->%d site=%d t%d state=%d\n",
                    (long long)i, e->seq, e->old, e->nw, e->site, e->tid,
                    e->state);
        }
    }
    fflush(stderr);
}

static int64_t mt_idle_rounds = 0;
static int64_t mt_idle_dump_n = -1; /* lazy env parse; -1 = unparsed */

void _emperor_mt_note_idle(void) {
    if (mt_idle_dump_n < 0) {
        mt_idle_dump_n = mt_env_budget("EMPEROR_IDLE_DUMP", 1000);
    }
    if (mt_idle_dump_n == 0) return;
    mt_idle_rounds++;
    if (mt_idle_rounds == mt_idle_dump_n) {
        char what[64];
        snprintf(what, sizeof what, "%lld idle rounds with live coroutines",
                 (long long)mt_idle_rounds);
        _emperor_mt_dump_parked(what);
        fflush(stdout);
        exit(63); /* lost-wake hang, reported — not a silent wait */
    }
}

void _emperor_mt_adopt_fd_woken(int64_t n) {
    while (n > 0) {
        EmperorCoroutine* co = (EmperorCoroutine*)_emperor_fd_take_woken();
        mt_glock_init();
        MT_LOCK(mt_glock);
        int slot = co_owner_get(co);
        if (slot < 0 || !sched_threads[slot].used) slot = 0;
        mt_runq_push_locked(slot, co);
        MT_UNLOCK(mt_glock);
        if (MT_ALD(sched_threads[slot].idle) || MT_ALD(sched_threads[slot].between_parked)) {
            MT_SIGNAL(sched_threads[slot].cv);
        }
        n--;
    }
}

/* gc.c interop: true while a parallel delta is in flight (collections
 * defer to the boundary; the allocator lock covers allocation). */
int _emperor_sched_parallel_active(void) {
    return mt_nthreads > 1 && MT_ALD(mt_delta_running);
}

void _emperor_sched_request_collect(void) {
    __atomic_store_n(&_emperor_gc_want_collect, 1, __ATOMIC_SEQ_CST);
}

/* Run ONE coroutine and file it by park status. */
static void mt_run_one_inner(EmperorCoroutine* co) {
    mt_trace("run_one co=%d state=%d steal=%d", co->seq, co_state_get(co), co->stealable);
    /* Stall diagnostic (a): one resume = one step of the current delta.
     * A spin (`while (!cond) wait -1;`) or a wake-livelock keeps resuming
     * without the delta ever converging; past the leash, report and stop
     * instead of hanging forever. Relaxed: only the overshoot matters. */
    if (mt_step_budget > 0 &&
        __atomic_fetch_add(&mt_step_count, 1, __ATOMIC_RELAXED) >= mt_step_budget) {
        mt_stall_abort(60,
                       "the current delta did not converge within its step budget",
                       "suspected zero-time loop or livelock, e.g. `while (!cond) wait -1;`");
        return;
    }
    int64_t status = _emperor_co_switch_in(co);
    mt_trace("run_one co=%d -> status=%d", co->seq, status);
    if (status == 3) {
        MT_AST(mt_stop, 1);
        return;
    }
    if (status == 0) {
        MT_LOCK(mt_glock);
        mt_timers_purge_co_locked(co);
        MT_UNLOCK(mt_glock);
        _emperor_co_destroy(co); /* unlinks, unpins, frees the stack;
                                    decrements mt_live_count */
        /* A waiting parent's spawn future just completed. */
        _emperor_mt_kick_futures();
        return;
    }
    if (status == 6) {
        /* Pure yield: still runnable in the current delta — owner's queue.
         * (A suppressed push here is LEGAL: a wake's phantom push may have
         * queued the coroutine inside its park window — that entry IS this
         * park's filing; the queue membership, not the flag's provenance,
         * is the invariant.) */
        MT_LOCK(mt_glock);
        int slot = co_owner_get(co);
        if (slot < 0 || !sched_threads[slot].used) slot = 0;
        mt_runq_push_locked(slot, co);
        MT_UNLOCK(mt_glock);
        return;
    }
    if (status == 2) return; /* fd-parked: the C waiter list owns it */
    if (status == 7) return; /* cond-parked: wake routing owns it */
    if (status == 8) return; /* woken mid-park: already in the owner's
                                queue — the wake's push IS the requeue */
    /* Bare `wait;`: owed the next delta at the current tick. This is the
     * ladder's ONLY legal fallthrough: a context that switched back must
     * carry CO_PARKED. Any other state means a park claim was lost or
     * clobbered (the class that used to fall through HERE silently and
     * mis-file the coroutine as a bare wait — re-queued every delta at
     * the same tick, pinning the virtual clock: the N>=5 steal x ESL
     * freeze). Fail loud instead of corrupting the schedule. */
    if (co_state_get(co) != CO_PARKED) {
        fflush(stdout);
        fprintf(stderr,
                "emperor sched: park-state invariant violated (co=%d state=%s) — dumping\n",
                co->seq, mt_co_state_name(co_state_get(co)));
        _emperor_mt_dump_parked("park-state invariant violation");
        fflush(stderr);
        exit(66);
    }
    MT_LOCK(mt_glock);
    mt_owed_push_locked(co);
    MT_UNLOCK(mt_glock);
}

static void mt_run_one(EmperorCoroutine* co) {
    mt_run_one_inner(co);
    __atomic_fetch_sub(&mt_inflight, 1, __ATOMIC_RELAXED);
}

/* Steal ONE never-run stealable coroutine from another thread's queue
 * (decision A: only CO_READY — never-run — entries may move). */
static EmperorCoroutine* mt_steal_one(void) {
    MT_LOCK(mt_glock);
    for (int i = 0; i < sched_thread_count; i++) {
        if (i == tls_thread_index || !sched_threads[i].used) continue;
        for (EmperorCoroutine* c = MT_ALD(sched_threads[i].runq_head); c; c = c->qnext) {
            if (co_state_get(c) == CO_READY && c->stealable) {
                EmperorCoroutine* taken = mt_runq_take_locked(i, c);
                if (taken) {
                    __atomic_fetch_add(&mt_inflight, 1, __ATOMIC_RELAXED);
                }
                MT_UNLOCK(mt_glock);
                return taken;
            }
        }
    }
    MT_UNLOCK(mt_glock);
    return NULL;
}

/* Adopt this thread's spawn inbox into its own queue (mid-delta spawns run
 * in the current delta on the spawning thread — affinity; stealable ones
 * can be stolen later). */
static void mt_adopt_own_spawns(void) {
    while (_emperor_co_spawn_count() > 0) {
        EmperorCoroutine* co = (EmperorCoroutine*)_emperor_co_take_spawn();
        MT_LOCK(mt_glock);
        mt_runq_push_locked(tls_thread_index, co);
        MT_UNLOCK(mt_glock);
    }
}

/* Idle protocol: park on the thread cv until work arrives, the delta ends
 * (every thread idle simultaneously) or the scheduler stops. Returns when
 * there is something to re-check. */
static void mt_idle_wait(void) {
    EmperorThreadInfo* me = &sched_threads[tls_thread_index];
    MT_LOCK(mt_glock);
    /* Re-check under the lock: work may have arrived since the caller's
     * empty checks (a wake/spawn pushes under this same lock). */
    if (MT_ALD(me->runq_head) || MT_ALD(mt_stop) || !MT_ALD(mt_delta_running)) {
        MT_UNLOCK(mt_glock);
        return;
    }
    if (!MT_ALD(me->idle)) {
        MT_AST(me->idle, 1);
        MT_AFA(mt_idle_count, 1);
        mt_trace("idle %d/%d (+between %d)", MT_ALD(mt_idle_count), mt_nthreads,
                 MT_ALD(mt_between_count));
        /* Convergence counts BOTH park kinds — a worker that slept through
         * the delta in worker_main's between-delta wait (no work routed to
         * it) is just as "done with this delta" as an idle_wait parker —
         * AND requires every queue empty: queued work parked on a sleeper
         * must not be left behind by the boundary (see
         * mt_any_runq_nonempty_locked). */
        if (MT_ALD(mt_idle_count) + MT_ALD(mt_between_count) >= mt_nthreads &&
            !mt_any_runq_nonempty_locked()) {
            /* Every thread is parked: the delta has converged. Wake main
             * (and everyone) — main proceeds to the boundary. between_parked
             * sleepers are NOT signaled: their predicate (!delta_running)
             * did not change, and waking them only burns a futex pair. */
            MT_AST(mt_delta_running, 0);
            mt_trace("last idle: broadcasting");
            for (int i = 0; i < sched_thread_count; i++) {
                if (sched_threads[i].used && !MT_ALD(sched_threads[i].between_parked)) {
                    MT_SIGNAL(sched_threads[i].cv);
                }
            }
            mt_trace("broadcast done");
        }
    }
    /* Sleep until routed work / delta end / stop — with a pre-futex spin
     * window (unlock/spin/relock, predicate re-checked under the lock):
     * work routed during the window is caught without a syscall, and the
     * canonical cond_wait below only runs when the window missed. */
    while (MT_ALD(me->runq_head) == NULL && MT_ALD(mt_delta_running) && !MT_ALD(mt_stop)) {
        MT_UNLOCK(mt_glock);
        int caught = 0;
        for (int64_t i = 0; i < mt_spin_budget(); i++) {
            for (int b = 0; b < MT_SPIN_BATCH; b++) mt_pause();
            if (MT_ALD(me->runq_head) != NULL ||
                !MT_ALD(mt_delta_running) ||
                MT_ALD(mt_stop)) {
                caught = 1;
                break;
            }
        }
        MT_LOCK(mt_glock);
        if (caught || !(MT_ALD(me->runq_head) == NULL && MT_ALD(mt_delta_running) && !MT_ALD(mt_stop))) {
            break; /* the spin window caught it — no futex pair burned */
        }
        mt_trace("sleeping");
        MT_WAIT(me->cv, mt_glock);
        mt_trace("woken");
    }
    if (MT_ALD(me->idle)) {
        MT_AST(me->idle, 0);
        MT_AFS(mt_idle_count, 1);
        if (MT_ALD(mt_idle_count) == 0) {
            /* Every straggler has now LEFT idle_wait — the barrier is
             * fully released and main may open the next delta. */
            mt_trace("all_parked signal from t%d", tls_thread_index);
            MT_SIGNAL(mt_all_parked_cv);
        }
    }
    MT_UNLOCK(mt_glock);
    mt_trace("idle_wait return");
}

/* The per-thread delta loop (main runs this too). */
/* ---- Order-shuffle detector (design doc §6.8: --shuffle-deltas) ----
 * EMPEROR_SHUFFLE_DELTAS=<seed>: at every delta start, SHUFFLE the ready
 * queue with a seeded LCG (single worker only — with N>1 the parallel
 * schedule already permutes the order naturally). A program whose output
 * differs across seeds depends on within-delta order: exactly what the
 * determinism subset (deferred components) forbids and what this detector
 * surfaces. Deterministic per seed; CI runs K seeds and diffs stdout. */
static unsigned long long mt_shuffle_state = 0;
static int mt_shuffle_on = 0;

static unsigned long long mt_shuffle_rand(void) {
    /* xorshift64* — deterministic, seedable */
    unsigned long long x = mt_shuffle_state;
    x ^= x >> 12;
    x ^= x << 25;
    x ^= x >> 27;
    mt_shuffle_state = x;
    return x * 2685821657736338717ULL;
}

static void mt_shuffle_maybe(int slot) {
    if (!mt_shuffle_on || mt_nthreads > 1) return;
    MT_LOCK(mt_glock);
    EmperorCoroutine* head = MT_ALD(sched_threads[slot].runq_head);
    int n = 0;
    for (EmperorCoroutine* c = head; c; c = c->qnext) n++;
    if (n >= 2) {
        /* collect, Fisher-Yates, relink */
        EmperorCoroutine** arr = (EmperorCoroutine**)malloc(sizeof(EmperorCoroutine*) * n);
        if (arr) {
            int i = 0;
            for (EmperorCoroutine* c = head; c; c = c->qnext) arr[i++] = c;
            for (i = n - 1; i > 0; i--) {
                int j = (int)(mt_shuffle_rand() % (unsigned long long)(i + 1));
                EmperorCoroutine* t = arr[i];
                arr[i] = arr[j];
                arr[j] = t;
            }
            for (i = 0; i < n - 1; i++) arr[i]->qnext = arr[i + 1];
            arr[n - 1]->qnext = NULL;
            MT_AST(sched_threads[slot].runq_head, arr[0]);
            sched_threads[slot].runq_tail = arr[n - 1];
            free(arr);
        }
    }
    MT_UNLOCK(mt_glock);
}

static void mt_delta_loop(void) {
    mt_trace("delta_loop enter");
    int shuffled_this_delta = 0;
    for (;;) {
        __atomic_fetch_add(&sched_threads[tls_thread_index].loop_iters, 1,
                           __ATOMIC_RELAXED);
        if (MT_ALD(mt_stop)) return;
        if (!MT_ALD(mt_delta_running)) return; /* delta converged */
        if (!shuffled_this_delta) {
            mt_shuffle_maybe(tls_thread_index);
            shuffled_this_delta = 1;
        }
        mt_adopt_own_spawns();
        MT_LOCK(mt_glock);
        EmperorCoroutine* co = mt_runq_pop_locked(tls_thread_index);
        if (co) {
            __atomic_fetch_add(&mt_inflight, 1, __ATOMIC_RELAXED);
        }
        MT_UNLOCK(mt_glock);
        if (!co) {
            co = mt_steal_one();
        }
        if (co) {
            mt_run_one(co);
            continue;
        }
        mt_idle_wait();
    }
}

/* ---- Worker threads ---- */

/* The worker's slot was PRE-ASSIGNED by mt_workers_start (which owns the
 * registry cursor and initialized the slot's cv); the thread adopts it via
 * its argument. Self-registration here would double-advance the cursor and
 * split the worker's identity from its signaled cv — the exact lost-wakeup
 * bug the first N>1 run exposed. */
static void mt_worker_adopt_slot(int slot) {
    char base_marker; /* the worker's own stack base: deepest local here */
    tls_thread_index = slot;
    sched_threads[slot].stack_bottom = (void*)&base_marker;
}

#if defined(EMPEROR_WIN_FIBERS)
static DWORD WINAPI mt_worker_main(LPVOID arg) {
#else
static void* mt_worker_main(void* arg) {
#endif
    mt_glock_init();
    __atomic_store_n(&mt_dbg, getenv("EMPEROR_MT_DEBUG") != NULL, __ATOMIC_RELAXED);
    mt_worker_adopt_slot((int)(intptr_t)arg);
    mt_trace("worker up");
    for (;;) {
        MT_LOCK(mt_glock);
        int wait_delta = !MT_ALD(mt_delta_running) && !MT_ALD(mt_stop);
        MT_UNLOCK(mt_glock);
        if (MT_ALD(mt_stop)) { mt_trace("worker exit"); break; }
        if (wait_delta) {
            /* Between deltas: park until the next delta opens (or work is
             * routed / the scheduler stops). Parking here counts toward
             * CONVERGENCE. The queue state is deliberately NOT part of the
             * predicate: a queued coroutine is un-parked two ways — the
             * push itself signals a between_parked sleeper (mt_runq_push_
             * locked), and the delta OPEN signals every nonempty queue —
             * so refusing to park over queued work bought nothing and cost
             * a traceless lock/unlock busy loop whenever a queue entry
             * outlived the delta that queued it (observed burning 8 cores
             * in the N=8 freeze: workers spinning between this predicate
             * and the top-of-loop re-check, no steps, no traces). */
            EmperorThreadInfo* me = &sched_threads[tls_thread_index];
            MT_LOCK(mt_glock);
            while (!MT_ALD(mt_delta_running) && !MT_ALD(mt_stop)) {
                MT_UNLOCK(mt_glock);
                int caught = 0;
                for (int64_t i = 0; i < mt_spin_budget(); i++) {
                    for (int b = 0; b < MT_SPIN_BATCH; b++) mt_pause();
                    if (MT_ALD(mt_delta_running) || MT_ALD(mt_stop)) {
                        caught = 1;
                        break;
                    }
                }
                MT_LOCK(mt_glock);
                if (caught || MT_ALD(mt_delta_running) || MT_ALD(mt_stop)) break;
                if (!MT_ALD(me->between_parked)) {
                    MT_AST(me->between_parked, 1);
                    MT_AFA(mt_between_count, 1);
                    if (MT_ALD(mt_idle_count) + MT_ALD(mt_between_count) >= mt_nthreads &&
                        !mt_any_runq_nonempty_locked()) {
                        /* The last parker from the between-delta side: the
                         * delta (if any was in flight) has converged. */
                        MT_AST(mt_delta_running, 0);
                        for (int i = 0; i < sched_thread_count; i++) {
                            if (sched_threads[i].used &&
                                !MT_ALD(sched_threads[i].between_parked)) {
                                MT_SIGNAL(sched_threads[i].cv);
                            }
                        }
                    }
                }
                MT_WAIT(me->cv, mt_glock);
                if (MT_ALD(me->between_parked)) {
                    MT_AST(me->between_parked, 0);
                    MT_AFS(mt_between_count, 1);
                }
            }
            if (MT_ALD(me->between_parked)) {
                MT_AST(me->between_parked, 0);
                MT_AFS(mt_between_count, 1);
            }
            MT_UNLOCK(mt_glock);
            continue;
        }
        mt_delta_loop();
    }
#if defined(EMPEROR_WIN_FIBERS)
    return 0;
#else
    return NULL;
#endif
}

/* EMPEROR_HANG_WATCHDOG=<seconds>: a debugging watchdog thread — if no
 * delta converges within the window, dump the scheduler state (thread
 * idles, run queues, timers, live coroutines) and abort(99). Never
 * started unless the env is set. POSIX-only (nanosleep/clock_gettime);
 * Windows builds simply have no watchdog. */
#if !defined(EMPEROR_WIN_FIBERS)
#include <time.h>
static int64_t mt_watchdog_deadline_ns(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000000ll + ts.tv_sec * 0 + (int64_t)ts.tv_nsec;
}
static int64_t mt_last_converge_ns = 0;
static void mt_watchdog_note_converge(void) {
    mt_last_converge_ns = mt_watchdog_deadline_ns();
}
static void* mt_watchdog_main(void* arg) {
    int64_t window = (int64_t)(intptr_t)arg * 1000000000ll;
    /* Baseline from launch: also catches "never converged once". */
    mt_last_converge_ns = mt_watchdog_deadline_ns();
    for (;;) {
        struct timespec ts = {0, 100 * 1000 * 1000};
        nanosleep(&ts, NULL);
        int64_t now = mt_watchdog_deadline_ns();
        if (mt_last_converge_ns != 0 && now - mt_last_converge_ns > window) {
            fprintf(stderr, "[watchdog] no delta convergence for %.1fs — dumping:\n",
                    (double)(now - mt_last_converge_ns) / 1e9);
            /* trylock, never lock: a watchdog that blocks on a stuck
             * mt_glock reports nothing at all — the exact failure mode it
             * exists to expose. On EBUSY, read the glibc mutex internals
             * raw (diagnostic-only; layout is stable on Linux) to name
             * the owner TID, then dump the unlocked-readable state. */
            if (MT_TRYLOCK(mt_glock) == 0) {
                fprintf(stderr, "[watchdog] delta_running=%d stop=%d idle_count=%d between=%d nthreads=%d\n",
                        MT_ALD(mt_delta_running), MT_ALD(mt_stop), mt_idle_count,
                        MT_ALD(mt_between_count), mt_nthreads);
                for (int i = 0; i < sched_thread_count; i++) {
                    if (!sched_threads[i].used) continue;
                    int n = 0;
                    for (EmperorCoroutine* c = MT_ALD(sched_threads[i].runq_head); c; c = c->qnext) n++;
                    fprintf(stderr, "[watchdog] t%d idle=%d between=%d runq=%d loop_iters=%lld\n", i,
                            MT_ALD(sched_threads[i].idle), MT_ALD(sched_threads[i].between_parked), n,
                            (long long)MT_ALD(sched_threads[i].loop_iters));
                }
                MT_UNLOCK(mt_glock);
            } else {
#if !defined(EMPEROR_WIN_FIBERS)
                fprintf(stderr,
                        "[watchdog] mt_glock STUCK: lock=%d owner_tid=%d "
                        "(owner 0 = corrupted unlock, other = live LWP in ps)\n",
                        mt_glock.__data.__lock, mt_glock.__data.__owner);
#else
                fprintf(stderr, "[watchdog] mt_glock STUCK (owner unknown)\n");
#endif
                fprintf(stderr, "[watchdog] delta_running=%d stop=%d idle_count=%d between=%d nthreads=%d\n",
                        MT_ALD(mt_delta_running), MT_ALD(mt_stop), mt_idle_count,
                        MT_ALD(mt_between_count), mt_nthreads);
                for (int i = 0; i < sched_thread_count; i++) {
                    if (!sched_threads[i].used) continue;
                    fprintf(stderr, "[watchdog] t%d idle=%d between=%d runq=%s\n", i,
                            sched_threads[i].idle, sched_threads[i].between_parked,
                            MT_ALD(sched_threads[i].runq_head) ? "nonempty" : "empty");
                }
            }
            fflush(stderr);
            abort();
        }
    }
    return NULL;
}
#else
/* No watchdog on Windows (see above); keep the shared call site compiling. */
#define mt_watchdog_note_converge() ((void)0)
#endif /* !EMPEROR_WIN_FIBERS */

static void mt_workers_start(void) {
    if (mt_workers_started) return;
    mt_workers_started = 1;
    mt_glock_init();
    {
        const char* sh = getenv("EMPEROR_SHUFFLE_DELTAS");
        if (sh && sh[0] != '\0' && sh[0] != '0') {
            mt_shuffle_state = strtoull(sh, NULL, 10);
            if (mt_shuffle_state == 0) mt_shuffle_state = 1;
            mt_shuffle_on = 1;
        }
    }
    {
        const char* wd = getenv("EMPEROR_HANG_WATCHDOG");
        if (wd && wd[0] != '\0' && wd[0] != '0') {
#if !defined(EMPEROR_WIN_FIBERS)
            pthread_t wdt;
            pthread_create(&wdt, NULL, mt_watchdog_main,
                           (void*)(intptr_t)atoi(wd));
#endif
        }
    }
    const char* env = getenv("EMPEROR_THREADS");
    int n = 1;
    if (env && env[0] != '\0') {
        n = atoi(env);
        if (n < 1) n = 1;
        if (n > EMPEROR_SCHED_MAX_THREADS) n = EMPEROR_SCHED_MAX_THREADS;
    }
    if (n <= 1) return; /* single worker: the calling (main) thread */
    /* main's slot must exist (it registers at first co_create/switch —
     * force it now so slot 0 == main before workers take 1..n-1). */
    sched_thread_register();
    /* The registry's lazy registration never marks main: without this the
     * delta-open broadcast below signals slot 0's cv too — a spurious wake
     * for a thread that never sleeps there mid-run, pure cv noise. */
    sched_threads[tls_thread_index].is_main = 1;
    for (int i = 1; i < n; i++) {
        int slot = sched_thread_count;
        if (slot >= EMPEROR_SCHED_MAX_THREADS) break;
        sched_threads[slot].used = 1;
        sched_threads[slot].stack_bottom = NULL; /* worker sets its own */
        sched_threads[slot].watermark = NULL;
        sched_threads[slot].is_main = 0;
        MT_COND_INIT(sched_threads[slot].cv);
        sched_thread_count++;
#if defined(EMPEROR_WIN_FIBERS)
        HANDLE h = CreateThread(NULL, 0, mt_worker_main, (LPVOID)(intptr_t)slot, 0, NULL);
        sched_threads[slot].thread_handle = h;
#else
        pthread_t tid;
        if (pthread_create(&tid, NULL, mt_worker_main, (void*)(intptr_t)slot) == 0) {
            sched_threads[slot].thread_handle = malloc(sizeof(pthread_t));
            *(pthread_t*)sched_threads[slot].thread_handle = tid;
            mt_nthreads++;
        } else {
            /* Worker creation failed: retire the slot, keep going with
             * the threads that did start. */
            sched_threads[slot].used = 0;
            sched_thread_count--;
        }
#endif
    }
}

/* Run the current delta to convergence across the worker set. Returns 0
 * when nothing is runnable anywhere, 1 when exit() was requested
 * mid-delta (the policy reads the code via _emperor_sched_exit_code). */
int64_t _emperor_mt_run_delta(void) {
    if (MT_ALD(mt_stop)) return 1;
    mt_glock_init();
    __atomic_store_n(&mt_dbg, getenv("EMPEROR_MT_DEBUG") != NULL, __ATOMIC_RELAXED);
    if (mt_step_budget < 0)
        mt_step_budget = mt_env_budget("EMPEROR_SCHED_STEP_BUDGET", 20000000);
    if (mt_pin_budget < 0)
        mt_pin_budget = mt_env_budget("EMPEROR_SCHED_PIN_BUDGET", 20000000);
    mt_step_count = 0; /* the (a) leash is per delta; workers are parked */
    mt_workers_start();

    /* Distribute MY inbox's never-run stealable coroutines round-robin
     * across the worker set (single-threaded: everything stays local —
     * identical to M4a). Mid-delta spawns stay with their spawner. */
    while (_emperor_co_spawn_count() > 0) {
        EmperorCoroutine* co = (EmperorCoroutine*)_emperor_co_take_spawn();
        MT_LOCK(mt_glock);
        if (co->stealable && mt_nthreads > 1) {
            mt_rr = (mt_rr + 1) % mt_nthreads;
            /* Map the rr cursor onto used slots in order. */
            int slot = 0;
            int seen = 0;
            for (int i = 0; i < sched_thread_count; i++) {
                if (!sched_threads[i].used) continue;
                if (seen == mt_rr) { slot = i; break; }
                seen++;
            }
            mt_runq_push_locked(slot, co);
        } else {
            mt_runq_push_locked(tls_thread_index, co);
        }
        MT_UNLOCK(mt_glock);
    }

    /* Open the parallel window: workers join the delta. The idle count is
     * ZERO here by construction (the previous delta's barrier fully
     * released below) — resetting it blindly would orphan stragglers still
     * inside idle_wait (idle flag set, never counted again) and the next
     * barrier would never reach nthreads.
     * A worker whose OWN queue is empty is NOT signaled: with more threads
     * than runnable coroutines (17 cos, 8 threads) the empty-queue workers
     * would otherwise burn a wake/lock/pop-fail/steal-scan/idle-sleep
     * round trip every delta — the measured N=8 convoy. They stay
     * between-delta parked, which CONVERGENCE counts (idle + between ==
     * nthreads), so sleeping through a workless delta cannot deadlock the
     * barrier; work routed to them mid-delta signals their cv at routing
     * time, and a queue filled before the open (the owed requeue, the
     * distributor) is exactly the non-empty case signaled here. */
    MT_LOCK(mt_glock);
    MT_AST(mt_delta_running, 1);
    for (int i = 0; i < sched_thread_count; i++) {
        if (sched_threads[i].used && !sched_threads[i].is_main &&
            MT_ALD(sched_threads[i].runq_head)) {
            MT_SIGNAL(sched_threads[i].cv);
        }
    }
    MT_UNLOCK(mt_glock);

    mt_delta_loop(); /* main participates */
    mt_watchdog_note_converge();
    mt_trace("delta converged (stop=%d)", MT_ALD(mt_stop));
    /* Barrier release: wait until every straggler has EXITED idle_wait
     * (count back to zero) — only then are the workers truly parked in
     * their between-delta waits and the boundary/GC may run. */
    MT_LOCK(mt_glock);
    mt_trace("barrier enter idle=%d", MT_ALD(mt_idle_count));
    while (MT_ALD(mt_idle_count) > 0 && !MT_ALD(mt_stop)) {
        MT_WAIT(mt_all_parked_cv, mt_glock);
        mt_trace("barrier woke idle=%d", MT_ALD(mt_idle_count));
    }
    MT_UNLOCK(mt_glock);
    mt_trace("barrier released");
    mt_scan_lost_parked();

    if (MT_ALD(mt_stop)) {
        return 1;
    }

    /* Stall diagnostic (b), evaluated at the boundary: the streak counts
     * consecutive converged deltas while time-waiters sit pending and no
     * timer ever makes real (later-deadline) progress — the clock is
     * pinned by zero-time churn, classically a `while (!cond) wait;`
     * retry loop (bare wait is owed its next delta AT the current tick,
     * so time cannot jump past it). An empty timer table means nobody
     * waits on time: not a pin — reset, and forget the deadline
     * high-water mark so a fresh wait cycle starts clean. */
    if (_emperor_mt_timer_earliest() >= 0) {
        mt_pin_streak++;
        if (mt_pin_budget > 0 && mt_pin_streak > mt_pin_budget) {
            char what[160];
            snprintf(what, sizeof what,
                     "the clock is pinned: %lld deltas converged with timer wait(s) pending but no timer firing",
                     (long long)mt_pin_streak);
            mt_stall_abort(61, what,
                           "suspected tick-pinning retry loop, e.g. `while (!cond) wait;`");
            return 1;
        }
    } else {
        mt_pin_streak = 0;
        mt_pin_max_deadline = INT64_MIN;
    }

    /* Boundary: every thread is parked here (stop-the-world by
     * construction) — drain a deferred collection if one is pending. */
    if (mt_nthreads > 1 || mt_gc_parked_count > 0) {
        if (mt_gc_parked_count > 0) {
            /* M5 v0 pressure valve (§10.1): allocation-blocked coroutines
             * parked on "wait for GC" — their parking converged this
             * delta, so this is the moment they parked FOR. Force the
             * collection (a parker demands a cycle even when the heap-goal
             * flag is quiet), then run the OOM predicate against the
             * smallest pending charge: if even it cannot fit the live
             * set, no collection can ever unblock anyone — the
             * deterministic OOM (exit 62), not a leash. Otherwise release
             * the parkers into the next delta. */
            extern void _emperor_gc_collect_now(void);
            extern int _emperor_gc_cap_blocks(uint64_t charge);
            extern void _emperor_gc_oom_abort(const char* why, uint64_t charge);
            size_t min_charge = mt_gc_parked_min_charge;
            __atomic_store_n(&_emperor_gc_want_collect, 1, __ATOMIC_RELAXED);
            _emperor_gc_collect_now();
            if (_emperor_gc_cap_blocks((uint64_t)min_charge)) {
                _emperor_gc_oom_abort(
                    "the reachable set cannot admit the smallest pending allocation",
                    (uint64_t)min_charge);
            }
            MT_LOCK(mt_glock);
            EmperorCoroutine* head = mt_gc_parked_head;
            mt_gc_parked_head = NULL;
            mt_gc_parked_count = 0;
            mt_gc_parked_min_charge = (size_t)-1;
            MT_UNLOCK(mt_glock);
            while (head) {
                EmperorCoroutine* next = head->qnext;
                head->qnext = NULL;
                _emperor_mt_wake(head);
                head = next;
            }
        } else {
            extern void _emperor_gc_collect_now(void);
            mt_trace("boundary gc enter");
            _emperor_gc_collect_now();
            mt_trace("boundary gc done");
        }
    }
    return 0;
}

/* Boundary invariant scan (debug): a CO_COND coroutine must be findable by
 * SOME waker — a run queue (woken/phantom), the owed list, the futures
 * set, the timer table, the GC valve or the fd waiters. One parked
 * nowhere is a consumed registration whose wake never landed — the lost
 * module behind the N>=5 freeze family. Catch it within one delta of the
 * loss, while the qlog ring still holds the transition. */
static int mt_co_findable_locked(EmperorCoroutine* co) {
    for (int i = 0; i < sched_thread_count; i++) {
        if (!sched_threads[i].used) continue;
        for (EmperorCoroutine* c = MT_ALD(sched_threads[i].runq_head); c; c = c->qnext) {
            if (c == co) return 1;
        }
    }
    if (co->on_owed) return 1;
    for (EmperorCoroutine* c = mt_futures_head; c; c = c->fnext) {
        if (c == co) return 1;
    }
    for (MtTimer* t = mt_timers; t; t = t->next) {
        if (t->co == co) return 1;
    }
    for (EmperorCoroutine* c = mt_gc_parked_head; c; c = c->qnext) {
        if (c == co) return 1;
    }
    for (EmperorFdWaiter* w = fd_waiters; w; w = w->next) {
        if (w->co == co) return 1;
    }
    return 0;
}

/* Boundary self-healing (safety net): a parked coroutine that NO waker can
 * find — CO_COND with no reachable filing, CO_PARKED with no owed entry —
 * is converted into a spurious wake (queue push / owed re-file) instead of
 * being silently lost. The wait loops are written to tolerate spurious
 * wakes (poll-and-recheck after every resume), so a repair costs one extra
 * poll and a delta of latency. The no-consume futures protocol removed the
 * interleavings that made this fire every few cycles (the stale in_futures
 * leak); with the protocol sound this scan is expected to stay SILENT —
 * a firing is a protocol regression to investigate, not noise to absorb.
 * EMPEROR_LOST_SCAN=1 keeps the loud diagnostic on top of the repair. */
static void mt_scan_lost_parked(void) {
    static int loud = -1;
    if (loud < 0) {
        const char* v = getenv("EMPEROR_LOST_SCAN");
        loud = (v && v[0] != '\0' && v[0] != '0') ? 1 : 0;
    }
    int repaired = 0;
    MT_LOCK(mt_glock);
    for (EmperorCoroutine* co = co_all_head; co; co = co->all_next) {
        int st = co_state_get(co);
        if (st != CO_COND && st != CO_PARKED) continue;
        if (co->queued || co->on_owed) continue; /* findable */
        if (co->lib_park) continue; /* filed on a Penguin-side waiter list
                                       (Fifo backpressure) — not ours to
                                       heal */
        if (mt_co_findable_locked(co)) continue;
        if (loud) {
            fprintf(stderr,
                    "[lost-repair] co=%d state=%s in_futures=%d future_park=%d — repairing\n",
                    co->seq, mt_co_state_name(st), co->in_futures,
                    co->future_park);
        }
        /* Drop any unreachable futures entry and STALE in_futures flag: the
         * idempotent register would otherwise skip the re-link (flag set,
         * entry unreachable) and the coroutine would park unfiled again —
         * the permanent repair ping-pong. With the flag cleared, the
         * post-resume wait loop re-links a fresh entry and the protocol
         * re-establishes itself. */
        {
            EmperorCoroutine** pp = &mt_futures_head;
            while (*pp) {
                if (*pp == co) {
                    *pp = co->fnext;
                    mt_futures_count--;
                    break;
                }
                pp = &(*pp)->fnext;
            }
            co->fnext = NULL;
            co->in_futures = 0;
        }
        if (st == CO_PARKED) {
            mt_owed_push_locked(co);
        } else {
            int slot = co_owner_get(co);
            if (slot < 0 || !sched_threads[slot].used) slot = 0;
            mt_runq_push_locked(slot, co);
        }
        repaired++;
    }
    MT_UNLOCK(mt_glock);
    if (repaired > 0) mt_trace("lost-repair n=%d", repaired);
}

/* Free every remaining coroutine (program exit; parked ones included)
 * and stop the workers. */
void _emperor_mt_destroy_all(void) {
    mt_trace("destroy_all");
    mt_glock_init();
    MT_LOCK(mt_glock);
    MT_AST(mt_stop, 1);
    MT_AST(mt_delta_running, 0);
    for (int i = 0; i < sched_thread_count; i++) {
        if (sched_threads[i].used) MT_SIGNAL(sched_threads[i].cv);
    }
    MT_UNLOCK(mt_glock);
    mt_trace("destroy: joining");
    /* Join the workers so nothing touches the heaps we are about to free. */
    for (int i = 0; i < sched_thread_count; i++) {
        if (sched_threads[i].used && sched_threads[i].thread_handle) {
#if defined(EMPEROR_WIN_FIBERS)
            WaitForSingleObject((HANDLE)sched_threads[i].thread_handle, INFINITE);
            CloseHandle((HANDLE)sched_threads[i].thread_handle);
#else
            pthread_join(*(pthread_t*)sched_threads[i].thread_handle, NULL);
            free(sched_threads[i].thread_handle);
#endif
            sched_threads[i].thread_handle = NULL;
        }
    }
    mt_trace("destroy: joined, freeing cos");
    while (co_all_head) {
        EmperorCoroutine* co = co_all_head;
        _emperor_co_destroy(co);
    }
    mt_trace("destroy: done");
    MT_LOCK(mt_glock);
    for (int i = 0; i < sched_thread_count; i++) {
        MT_AST(sched_threads[i].runq_head, NULL);
        sched_threads[i].runq_tail = NULL;
    }
    mt_owed_head = mt_owed_tail = NULL;
    for (EmperorCoroutine* c = co_all_head; c; c = c->all_next) c->on_owed = 0;
    mt_futures_head = NULL;
    mt_futures_count = 0;
    mt_gc_parked_head = NULL;
    mt_gc_parked_count = 0;
    mt_gc_parked_min_charge = (size_t)-1;
    while (mt_timers) {
        MtTimer* t = mt_timers;
        mt_timers = t->next;
        free(t);
    }
    MT_AST(mt_stop, 0);
    MT_UNLOCK(mt_glock);
}

/* Public park pair for library-level condition waits (Fifo backpressure):
 * park_begin claims CO_COND (no lost wake after it returns), the caller
 * registers in ITS waiter list, switch_away parks. */
void _emperor_mt_park_begin(void) { mt_park_begin(); }
void _emperor_mt_switch_away(void) { mt_switch_away(); }
