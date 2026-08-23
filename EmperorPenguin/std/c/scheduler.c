/* EmperorPenguin coroutine scheduler (RTL-ports / async runtime).
 *
 * Stackful coroutines: every initial routine and every `async f()` spawn runs
 * on its own mmap'd stack; `wait` switches back to the scheduler. The
 * scheduler is a delta-round loop mirroring the BabyPenguin SimScheduler
 * semantics (the reference implementation):
 *
 *   round: run every coroutine queued at round start (a coroutine that calls
 *   wait() parks and is re-queued for the NEXT round — wait means "yield one
 *   delta round", and parked coroutines re-poll their predicates each round);
 *   fire expired timers (entry removal only — readiness is observed by
 *   pollers); when nothing progresses and timers remain, advance the
 *   simulation clock to the earliest deadline; when nothing progresses and no
 *   timers remain, quiescence detection: fingerprint every live coroutine's
 *   frozen stack and exit normally once the fingerprint repeats unchanged for
 *   two consecutive idle rounds (no external event sources in v1, so
 *   "waiting forever" and "deadlock" are indistinguishable — quiescence is a
 *   normal exit, matching BabyPenguin).
 *
 * Coroutines interoperate with the conservative GC: each stack block is
 * registered as a scan region (the EmperorCoroutine struct lives at the top
 * of the block so the saved ucontext — which holds the coroutine's spilled
 * callee-saved registers — is scanned too), and gc.c scans the MAIN stack
 * only up to the watermark recorded at the last switch when a collection
 * triggers on a coroutine stack (see _emperor_gc_alt_stack_scan).
 *
 * POSIX ucontext provides the context switch (Linux first; Windows builds get
 * a sequential fallback — no interleaving, wait is a no-op — until fibers are
 * wired up).
 */

#include "emperor_gc.h"
#include "emperor_interop.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <setjmp.h>

#if defined(_WIN32)
#define EMPEROR_NO_UCONTEXT 1
#else
#include <ucontext.h>
#include <sys/mman.h>
#endif

#define EMPEROR_CO_STACK_SIZE (32 * 1024 * 1024)

/* ---- GC interop (gc.c reads these) ----
 * Non-NULL while coroutines exist: when a collection triggers on a coroutine
 * stack, gc.c scans the main stack only up to this watermark (recorded, with
 * a setjmp register flush, each time the scheduler switches away from the
 * main stack) instead of scanning down to the coroutine's stack pointer,
 * which lives in an unrelated mmap block. */
void* _emperor_gc_main_watermark = NULL;
int _emperor_gc_on_coroutine = 0;

/* ---- try/catch (sjlj) ---- */

typedef struct EmperorTryFrame {
    struct EmperorTryFrame* prev;
    jmp_buf jb;
} EmperorTryFrame;

/* Pending error payload for the catch handler: set by throw, read by the
 * catch-entry code. Registered as a GC root so the message string survives
 * the longjmp window. */
static char* _emperor_throw_msg = NULL;
static int64_t _emperor_throw_code = 0;
static char** _emperor_throw_msg_root = &_emperor_throw_msg;

/* ---- Coroutine ---- */

enum {
    CO_READY = 1,   /* queued, never ran or re-queued after park */
    CO_RUNNING = 2, /* currently executing */
    CO_PARKED = 3,  /* stopped in wait(), re-queued for next round */
    CO_FINISHED = 4 /* entry returned; pending free */
};

typedef struct EmperorCoroutine {
#ifdef EMPEROR_NO_UCONTEXT
    char ctx[64]; /* placeholder keeps layout code uniform */
#else
    ucontext_t ctx; /* saved callee-saved registers live here (scanned) */
#endif
    char* block;    /* mmap base of the whole stack block */
    char* stack_hi; /* block end (just past this struct) */
    char* sp_park;  /* stack pointer captured at park (fingerprint start) */
    void (*entry)(void*); /* consumed at the coroutine's first switch */
    void* entry_arg;
    struct EmperorCoroutine* qnext; /* ready / next-round queue link */
    struct EmperorCoroutine* anext; /* all-coroutines list (creation order) */
    int state;
    int seq;              /* creation index: stable fingerprint ordering */
    EmperorTryFrame* try_top;
} EmperorCoroutine;

static EmperorCoroutine* sched_current = NULL; /* NULL on main/scheduler stack */
static EmperorCoroutine* ready_head = NULL;
static EmperorCoroutine* ready_tail = NULL;
static EmperorCoroutine* next_head = NULL; /* next-round queue */
static EmperorCoroutine* next_tail = NULL;
static EmperorCoroutine* all_cos = NULL; /* creation-order list */
static int co_seq_counter = 0;

#ifndef EMPEROR_NO_UCONTEXT
static ucontext_t sched_ctx; /* scheduler switchback point */
#endif

/* The coroutine body: runs the recorded entry, then marks the coroutine
 * finished. uc_link = &sched_ctx returns control to the scheduler loop. */
static void co_trampoline(void) {
    EmperorCoroutine* self = sched_current;
    void (*entry)(void*) = self->entry;
    void* arg = self->entry_arg;
    self->entry = NULL;
    self->entry_arg = NULL;
    entry(arg);
    self->state = CO_FINISHED;
    /* return → uc_link → scheduler */
}

static int sched_exit_requested = 0;
static int sched_exit_code = 0;

/* ---- Simulation clock / activity ---- */

static int64_t sim_now_tick = 0;
static int64_t sim_round = 0;
static int64_t sim_activity = 0;
static int64_t round_start_activity = 0;
static int last_round_quiet = 0;

static int64_t* timer_ticks = NULL; /* sorted ascending */
static size_t timer_count = 0;
static size_t timer_cap = 0;

static void enqueue(EmperorCoroutine** head, EmperorCoroutine** tail, EmperorCoroutine* co) {
    co->qnext = NULL;
    if (*tail) {
        (*tail)->qnext = co;
        *tail = co;
    } else {
        *head = co;
        *tail = co;
    }
}

static EmperorCoroutine* dequeue(EmperorCoroutine** head, EmperorCoroutine** tail) {
    EmperorCoroutine* co = *head;
    if (!co) return NULL;
    *head = co->qnext;
    if (!*head) *tail = NULL;
    co->qnext = NULL;
    return co;
}

static void timer_insert(int64_t tick) {
    if (timer_count == timer_cap) {
        size_t ncap = timer_cap ? timer_cap * 2 : 16;
        int64_t* nt = (int64_t*)realloc(timer_ticks, ncap * sizeof(int64_t));
        if (!nt) {
            fprintf(stderr, "emperor sched: out of memory (timer table)\n");
            abort();
        }
        timer_ticks = nt;
        timer_cap = ncap;
    }
    size_t i = timer_count;
    while (i > 0 && timer_ticks[i - 1] > tick) {
        timer_ticks[i] = timer_ticks[i - 1];
        i--;
    }
    timer_ticks[i] = tick;
    timer_count++;
}

/* Remove every timer entry with deadline <= now; returns how many fired. */
static int timers_fire(void) {
    int fired = 0;
    while (timer_count > 0 && timer_ticks[0] <= sim_now_tick) {
        memmove(timer_ticks, timer_ticks + 1, (timer_count - 1) * sizeof(int64_t));
        timer_count--;
        fired++;
    }
    return fired;
}

static void co_destroy(EmperorCoroutine* co) {
    /* unlink from the all-list */
    EmperorCoroutine** p = &all_cos;
    while (*p) {
        if (*p == co) {
            *p = co->anext;
            break;
        }
        p = &(*p)->anext;
    }
    _emperor_gc_scan_remove(co->block);
#ifndef EMPEROR_NO_UCONTEXT
    munmap(co->block, EMPEROR_CO_STACK_SIZE);
#else
    free(co->block);
#endif
}

/* The coroutine body: runs the pending entry, then marks the coroutine
 * finished. uc_link = &sched_ctx returns control to the scheduler loop. */
static EmperorCoroutine* co_create(void (*entry)(void*)) {
    size_t total = EMPEROR_CO_STACK_SIZE;
    char* block;
#ifndef EMPEROR_NO_UCONTEXT
    block = (char*)mmap(NULL, total, PROT_READ | PROT_WRITE,
                        MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    if (block == MAP_FAILED) {
        fprintf(stderr, "emperor sched: cannot allocate coroutine stack\n");
        abort();
    }
#else
    block = (char*)calloc(1, total);
#endif
    EmperorCoroutine* co = (EmperorCoroutine*)(block + total - sizeof(EmperorCoroutine));
    memset(co, 0, sizeof(EmperorCoroutine));
    co->block = block;
    co->stack_hi = block + total;
    co->state = CO_READY;
    co->seq = co_seq_counter++;
    co->entry = entry;
    co->entry_arg = NULL; /* set by caller */
    co->anext = all_cos;
    all_cos = co;
    /* Whole block is one GC scan region: live frames, the saved ucontext
     * (spilled registers) and the struct itself are all covered; bytes below
     * the parked sp are stale garbage, which a conservative collector safely
     * over-retains. */
    _emperor_gc_scan_add(block, total);
#ifndef EMPEROR_NO_UCONTEXT
    if (getcontext(&co->ctx) == -1) {
        fprintf(stderr, "emperor sched: getcontext failed\n");
        abort();
    }
    co->ctx.uc_stack.ss_sp = block;
    co->ctx.uc_stack.ss_size = total - sizeof(EmperorCoroutine) - 64;
    co->ctx.uc_link = &sched_ctx;
    makecontext(&co->ctx, co_trampoline, 0);
#endif
    enqueue(&ready_head, &ready_tail, co);
    return co;
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
    self->state = CO_PARKED;
    /* record the frozen stack extent for the quiescence fingerprint */
    char marker;
    self->sp_park = (char*)&marker;
#ifndef EMPEROR_NO_UCONTEXT
    swapcontext(&self->ctx, &sched_ctx);
#else
    /* Sequential fallback (no real coroutines): time-travel to the earliest
     * pending timer deadline so timer waits (`wait n tick` poll loops) still
     * terminate; concurrency semantics are not preserved. */
    if (timer_count > 0 && timer_ticks[0] > sim_now_tick) {
        sim_now_tick = timer_ticks[0];
    }
    self->state = CO_RUNNING;
#endif
}

int64_t _emperor_sim_now(void) { return sim_now_tick; }
int64_t _emperor_sim_delta(void) { return sim_round; }
void _emperor_sim_activity(void) { sim_activity++; }
int _emperor_sim_settled(void) {
    return last_round_quiet && (sim_activity == round_start_activity) ? 1 : 0;
}

/* Register a timer deadline (the future object lives in Penguin-land; only
 * the deadline is tracked — readiness is observed by pollers). */
void _emperor_timer_at(int64_t deadline) {
    if (deadline < sim_now_tick) deadline = sim_now_tick;
    timer_insert(deadline);
}

/* exit(): from a coroutine, unwind to the scheduler and end the program with
 * the given code; from the main stack, exit immediately. */
void _emperor_sched_exit(int code) {
    if (sched_current) {
        sched_exit_requested = 1;
        sched_exit_code = code;
#ifndef EMPEROR_NO_UCONTEXT
        swapcontext(&sched_current->ctx, &sched_ctx);
#else
        exit(code);
#endif
        return;
    }
    exit(code);
}

/* ---- try/catch (sjlj) ----
 * The buffer is 256 bytes of raw malloc'd memory owned by the compiled try
 * statement. try_enter pushes a frame and returns setjmp's value: 0 on the
 * normal path, 1 when a throw longjmps back. try_leave pops on the normal
 * path; throw pops the frame itself, so the catch path must NOT call leave. */
int _emperor_try_enter(void* jb_raw) {
    EmperorCoroutine* self = sched_current;
    if (!self) {
        fprintf(stderr, "emperor sched: try/catch outside a coroutine\n");
        exit(1);
    }
    EmperorTryFrame* f = (EmperorTryFrame*)jb_raw;
    f->prev = self->try_top;
    self->try_top = f;
    return setjmp(f->jb);
}

void _emperor_try_leave(void) {
    EmperorCoroutine* self = sched_current;
    if (self && self->try_top) {
        self->try_top = self->try_top->prev;
    }
}

/* Throw a runtime error: if the current coroutine has an enclosing try,
 * stash the payload and longjmp to the innermost handler; otherwise the
 * error is uncaught — report and exit non-zero (flushing buffered stdout
 * first, matching the BabyPenguin diagnostic shape). */
void _emperor_throw_runtime_error(const char* msg, int64_t code) {
    _emperor_throw_msg = (char*)msg;
    _emperor_throw_code = code;
    if (sched_current && sched_current->try_top) {
        EmperorTryFrame* f = sched_current->try_top;
        sched_current->try_top = f->prev;
        longjmp(f->jb, 1);
    }
    fflush(stdout);
    fprintf(stderr, "Uncaught runtime error: %s (code %lld)\n", msg ? msg : "?",
            (long long)code);
    _emperor_sched_exit(1);
}

const char* _emperor_throw_get_msg(void) { return _emperor_throw_msg; }
int64_t _emperor_throw_get_code(void) { return _emperor_throw_code; }

/* ---- Quiescence fingerprint ----
 * Hash every live coroutine's frozen stack ([parked sp, stack top]) plus the
 * saved sp, in creation order. A coroutine that keeps re-running the same
 * polling loop produces an identical fingerprint; a loop that mutates locals
 * (observation counters, spin variables) changes its stack bytes and counts
 * as forward motion, exactly like BabyPenguin's per-register snapshots. */
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

static uint64_t sched_fingerprint(void) {
    uint64_t h = 0x1234567890ABCDEFULL;
    for (EmperorCoroutine* co = all_cos; co; co = co->anext) {
        h = fp_hash_bytes(h, (char*)&co, sizeof(EmperorCoroutine*));
        const char* lo = co->sp_park ? co->sp_park : co->block;
        if (lo < co->stack_hi) {
            h = fp_hash_bytes(h, lo, (size_t)(co->stack_hi - lo));
        }
    }
    return h;
}

/* ---- The scheduler loop ---- */

int _emperor_sched_run(void) {
    /* Make the pending-throw message slot a GC root for this program. */
    _emperor_gc_add_root((void**)&_emperor_throw_msg);

    static uint64_t last_fp = 0;
    static int fp_valid = 0;

    while (!sched_exit_requested) {
        /* No work at all: program finished. */
        if (!ready_head && !next_head && timer_count == 0 && !all_cos) break;

        sim_round++;
        round_start_activity = sim_activity;

        int progress = 0;

        /* Delta round: run everything queued at round start. Parks go to the
         * next-round queue (they resume next round); newly spawned coroutines
         * also only run next round. */
        while (ready_head) {
            EmperorCoroutine* co = dequeue(&ready_head, &ready_tail);
#ifndef EMPEROR_NO_UCONTEXT
            sched_current = co;
            co->state = CO_RUNNING;
            /* Flush main-stack callee-saved registers and record the main
             * stack watermark for a possibly-GC-triggering coroutine. */
            jmp_buf switch_flush;
            setjmp(switch_flush);
            _emperor_gc_main_watermark = (char*)&switch_flush;
            _emperor_gc_on_coroutine = 1;
            swapcontext(&sched_ctx, &co->ctx);
            _emperor_gc_on_coroutine = 0;
            sched_current = NULL;
            _emperor_gc_main_watermark = NULL;
#else
            /* Sequential fallback: run the entry inline on this stack (the
             * entry may "park" via the no-op wait, but it then runs to
             * completion in one go). */
            sched_current = co;
            co->state = CO_RUNNING;
            if (co->entry) {
                void (*entry)(void*) = co->entry;
                void* arg = co->entry_arg;
                co->entry = NULL;
                co->entry_arg = NULL;
                entry(arg);
            }
            co->state = CO_FINISHED;
            sched_current = NULL;
#endif
            if (co->state == CO_FINISHED) {
                progress = 1;
                co_destroy(co);
            } else {
                /* CO_PARKED (or CO_READY if it never ran — only possible via
                 * the inline fallback path): re-queue for the next round. */
                enqueue(&next_head, &next_tail, co);
            }
            if (sched_exit_requested) break;
        }
        if (sched_exit_requested) break;

        /* Rotate the next-round queue in as the new current round. */
        ready_head = next_head;
        ready_tail = next_tail;
        next_head = NULL;
        next_tail = NULL;

        /* Fire already-due timers (usually none — time advances below). */
        if (timers_fire() > 0) progress = 1;

        last_round_quiet = (sim_activity == round_start_activity) ? 1 : 0;

        if (progress) {
            fp_valid = 0;
            continue;
        }

        if (timer_count > 0) {
            /* Idle with timers pending: jump the clock to the earliest
             * deadline and let the next round's pollers observe it. */
            if (timer_ticks[0] > sim_now_tick) sim_now_tick = timer_ticks[0];
            timers_fire();
            continue;
        }

        /* No progress, no timers: quiescence detection. Transactions that
         * flowed this round (channel writes, event emits) keep the program
         * alive; an unchanged fingerprint two rounds in a row is a normal
         * exit (v1 has no external event sources). */
        if (sim_activity != round_start_activity) {
            fp_valid = 0;
            continue;
        }
        uint64_t fp = sched_fingerprint();
        if (fp_valid && fp == last_fp) break;
        last_fp = fp;
        fp_valid = 1;
    }

    /* Free remaining coroutine stacks (process is about to exit or main
     * continues without the scheduler). */
    while (all_cos) {
        EmperorCoroutine* co = all_cos;
        co_destroy(co);
    }
    ready_head = ready_tail = next_head = next_tail = NULL;
    timer_count = 0;
    return sched_exit_requested ? sched_exit_code : 0;
}
