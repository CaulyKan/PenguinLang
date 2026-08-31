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
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <setjmp.h>

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

/* ---- GC interop (gc.c reads these) ----
 * Non-NULL while coroutines exist: when a collection triggers on a coroutine
 * stack, gc.c scans the main stack only up to this watermark (recorded, with
 * a setjmp register flush, each time the scheduler switches away from the
 * main stack) instead of scanning down to the coroutine's stack pointer,
 * which lives in an unrelated mmap block. */
void* _emperor_gc_main_watermark = NULL;

/* gc.c: narrow a registered scan region to its live range. */
void _emperor_gc_scan_set_live(void* base, void* live_lo);
int _emperor_gc_on_coroutine = 0;

/* ---- try/catch (sjlj) ---- */

typedef struct EmperorTryFrame {
    struct EmperorTryFrame* prev;
    jmp_buf* jb; /* points into the per-site table; owned by the site */
} EmperorTryFrame;

/* Pending error payload for the catch handler: set by throw, read by the
 * catch-entry code. Registered as a GC root (lazily, on first throw — the
 * old registration point was the C scheduler loop, which no longer exists)
 * so the message string survives the longjmp window. */
static char* _emperor_throw_msg = NULL;
static int64_t _emperor_throw_code = 0;
static int _emperor_throw_msg_root_done = 0;

/* ---- Coroutine ---- */

enum {
    CO_READY = 1,   /* created, never ran (sitting in the spawn inbox or a
                       Penguin queue) */
    CO_RUNNING = 2, /* currently executing */
    CO_PARKED = 3,  /* stopped in wait(), re-queued for next round */
    CO_FINISHED = 4, /* entry returned; pending free */
    CO_FD_PARKED = 5 /* parked on an fd (fd_waiters list); NOT re-queued —
                        the Penguin loop re-queues it when the fd turns
                        ready (_fd_poll/_fd_take_woken) */
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
    int state;
    int seq;              /* creation index (stable identity for the Penguin
                             loop's all-live registry; read via _co_seq) */
    EmperorTryFrame* try_top;
} EmperorCoroutine;

static EmperorCoroutine* sched_current = NULL; /* NULL on main/scheduler stack */

#if defined(EMPEROR_UCONTEXT)
static ucontext_t sched_ctx; /* scheduler switchback point */
#elif defined(EMPEROR_WIN_FIBERS)
static void* sched_fiber = NULL; /* the main thread's fiber (scheduler home) */
#endif

static int sched_exit_requested = 0;
static int sched_exit_code = 0;

/* ---- Spawn inbox ----
 * co_create parks fresh handles here (FIFO, qnext-linked); the Penguin loop
 * drains them at round boundaries and after every switch — a coroutine
 * spawned during round N may run in round N, exactly like the old C loop's
 * enqueue-into-ready. main() spawns initial routines through
 * _emperor_co_spawn_fn0 before the loop is entered; async spawns from
 * coroutine context land here mid-round. */
static EmperorCoroutine* spawn_inbox_head = NULL;
static EmperorCoroutine* spawn_inbox_tail = NULL;

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
    entry(arg);
    self->state = CO_FINISHED;
}

#ifndef EMPEROR_WIN_FIBERS
static void co_trampoline(void) {
    co_run_entry(sched_current);
    /* return → uc_link → scheduler */
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

static EmperorCoroutine* co_create(void (*entry)(void*)) {
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
    co->state = CO_READY;
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
    co->ctx.uc_link = &sched_ctx;
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
    co->state = CO_READY;
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
    co->state = CO_READY;
    co->seq = co_seq_counter++;
    co->entry = entry;
    co->entry_arg = NULL; /* set by caller */
    _emperor_gc_scan_add((char*)co,
                         ((sizeof(EmperorCoroutine) + 7u) & ~(size_t)7u));
#endif
    inbox_push(co);
    return co;
}

void _emperor_co_destroy(void* handle) {
    EmperorCoroutine* co = (EmperorCoroutine*)handle;
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
    co->state = CO_RUNNING;
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
    ensure_sched_home();
    /* Flush main-stack callee-saved registers and record the main stack
     * watermark for a possibly-GC-triggering coroutine. */
    jmp_buf switch_flush;
    setjmp(switch_flush);
    _emperor_gc_main_watermark = (char*)&switch_flush;
    _emperor_gc_on_coroutine = 1;
    sched_switch_to_co(co);
    _emperor_gc_on_coroutine = 0;
    sched_current = NULL;
    _emperor_gc_main_watermark = NULL;
    if (sched_exit_requested) return 3;
    if (co->state == CO_FINISHED) return 0;
    if (co->state == CO_FD_PARKED) return 2;
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
    co->state = CO_FINISHED;
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
    self->state = CO_RUNNING;
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
    self->state = CO_FD_PARKED;
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
    self->state = CO_RUNNING;
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
        sched_exit_requested = 1;
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
void _emperor_throw_runtime_error(const char* msg, int64_t code) {
    if (!_emperor_throw_msg_root_done) {
        /* Make the pending-throw message slot a GC root for this program —
         * the message must survive the longjmp window until the catch
         * handler reads it. Registered on first throw (any context: main
         * stack or coroutine) rather than at scheduler entry. */
        _emperor_gc_add_root((void**)&_emperor_throw_msg);
        _emperor_throw_msg_root_done = 1;
    }
    _emperor_throw_msg = (char*)msg;
    _emperor_throw_code = code;
    EmperorTryFrame** top = _emperor_try_top_slot();
    if (*top) {
        EmperorTryFrame* f = *top;
        *top = f->prev;
        jmp_buf* jb = f->jb;
        free(f);
        _longjmp(*jb, 1);
    }
    fflush(stdout);
    fprintf(stderr, "Uncaught runtime error: %s (code %lld)\n", msg ? msg : "?",
            (long long)code);
    _emperor_sched_exit(1);
}

const char* _emperor_throw_get_msg(void) { return _emperor_throw_msg; }
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
