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
 *   timers remain but fd waiters exist, block in poll() on the registered
 *   descriptors — fd readiness injects the next delta (external event
 *   sources); only when nothing progresses, no timers remain and no fd
 *   waiters exist, quiescence detection runs: fingerprint every live
 *   coroutine's frozen stack and exit normally once the fingerprint repeats
 *   unchanged for two consecutive idle rounds.
 *
 * Coroutines interoperate with the conservative GC: each stack block is
 * registered as a scan region (the EmperorCoroutine struct lives at the top
 * of the block so the saved ucontext — which holds the coroutine's spilled
 * callee-saved registers — is scanned too), and gc.c scans the MAIN stack
 * only up to the watermark recorded at the last switch when a collection
 * triggers on a coroutine stack (see _emperor_gc_alt_stack_scan).
 *
 * POSIX ucontext provides the context switch on Linux; Windows builds use
 * Win32 fibers (CreateFiberEx/SwitchToFiber) with the same single-threaded
 * discipline — user code only ever runs inside one fiber at a time, so the
 * conservative GC needs no thread synchronization (see the fiber notes at
 * co_create/fd_poll_all). stdin/stdout readiness is level-probed with
 * PeekNamedPipe; there is no epoll-for-pipes on Windows, so idle blocking is
 * a 2 ms re-probe loop. Anonymous pipes expose no writable-space query, so
 * fd write waits always report ready and the write syscall itself blocks
 * when the pipe is full — the same ordered backpressure the POLLOUT park
 * gives on POSIX, because frames are written strictly in order by a single
 * writer anyway.
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
    CO_FINISHED = 4, /* entry returned; pending free */
    CO_FD_PARKED = 5 /* parked on an fd (fd_waiters list); NOT re-queued —
                        fd_poll_all enqueues it when the fd turns ready */
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

#if defined(EMPEROR_UCONTEXT)
static ucontext_t sched_ctx; /* scheduler switchback point */
#elif defined(EMPEROR_WIN_FIBERS)
static void* sched_fiber = NULL; /* the main thread's fiber (scheduler home) */
#endif

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
    _emperor_gc_scan_remove(co->block); /* NULL: fiber never ran — no stack region */
#if defined(EMPEROR_UCONTEXT)
    munmap(co->block, EMPEROR_CO_STACK_SIZE);
#elif defined(EMPEROR_WIN_FIBERS)
    _emperor_gc_scan_remove((char*)co); /* the struct region (entry_arg, reg spills) */
    if (co->fiber) DeleteFiber(co->fiber);
    free(co);
#else
    free(co->block);
#endif
}

static EmperorCoroutine* co_create(void (*entry)(void*)) {
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
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
    co->anext = all_cos;
    all_cos = co;
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
#else
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
    co->anext = all_cos;
    all_cos = co;
    _emperor_gc_scan_add((char*)co,
                         ((sizeof(EmperorCoroutine) + 7u) & ~(size_t)7u));
#endif
    enqueue(&ready_head, &ready_tail, co);
    return co;
#else
    /* Sequential fallback (no real context switch available): the whole
     * stack block is one calloc'd buffer and entries run inline to
     * completion in the scheduler loop. */
    size_t total = EMPEROR_CO_STACK_SIZE;
    char* block = (char*)calloc(1, total);
    if (!block) {
        fprintf(stderr, "emperor sched: cannot allocate coroutine stack\n");
        abort();
    }
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
    _emperor_gc_scan_add(block, total);
    _emperor_gc_scan_set_live(block, block + total);
    enqueue(&ready_head, &ready_tail, co);
    return co;
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

/* ---- External fd event sources ----
 * A coroutine may park on a file descriptor (readable or writable) instead of
 * the round queue. Such coroutines are external event sources: while any fd
 * waiter exists, quiescence no longer exits the program — the scheduler
 * blocks in poll() over the registered descriptors and the readiness of any
 * of them injects the next delta round (the waiter is re-queued and the loop
 * continues). This is the epoll/poll integration point that retires the v1
 * "quiescence == exit because no external event sources exist" deviation of
 * rtl-ports-design.md §E for programs that actually wait on fds; programs
 * that never do keep the exact previous behavior. */

typedef struct EmperorFdWaiter {
    struct EmperorFdWaiter* next;
    EmperorCoroutine* co;
    int fd;
    int for_write; /* 0 = wait readable, 1 = wait writable */
} EmperorFdWaiter;

static EmperorFdWaiter* fd_waiters = NULL;

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

/* Poll every registered fd. Ready waiters are unlinked and their coroutines
 * queued for the next delta round. Returns how many were woken. timeout_ms
 * < 0 blocks until an event (or EINTR); 0 is a pure readiness probe. */
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

static int fd_poll_all(int timeout_ms) {
    if (!fd_waiters) return 0;
    for (;;) {
        int woken = 0;
        for (EmperorFdWaiter* w = fd_waiters; w;) {
            EmperorFdWaiter* wnext = w->next; /* fd_unlink frees w */
            if (win_fd_ready(w->fd, w->for_write)) {
                EmperorCoroutine* co = w->co;
                fd_unlink(w);
                enqueue(&next_head, &next_tail, co);
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
static int fd_poll_all(int timeout_ms) {
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
    int rc = poll(pfds, (nfds_t)n, timeout_ms);
    int woken = 0;
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
            enqueue(&next_head, &next_tail, co);
            woken++;
        }
    }
    free(pfds);
    free(order);
    return woken; /* rc <= 0 (EINTR / error): caller re-enters */
}
#endif /* fd_poll_all variants */

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
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
        co_switch_to_sched(sched_current);
#else
        exit(code);
#endif
        return;
    }
    exit(code);
}

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
        /* A fiber coroutine that never ran has no stack region yet (bounds
         * are discovered at first switch): its contribution is constant,
         * like a never-touched mmap page on POSIX. */
        if (co->stack_hi && lo && lo < co->stack_hi) {
            h = fp_hash_bytes(h, lo, (size_t)(co->stack_hi - lo));
        }
    }
    return h;
}

/* ---- The scheduler loop ---- */

int _emperor_sched_run(void) {
#ifdef EMPEROR_WIN_FIBERS
    /* The scheduler runs on the main thread converted to a fiber: every
     * SwitchToFiber must originate from — and return to — a fiber context.
     * (A second call after ConvertFiberToThread-less nesting sees
     * ERROR_ALREADY_FIBER and reuses the current fiber.) */
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
#if defined(EMPEROR_UCONTEXT) || defined(EMPEROR_WIN_FIBERS)
            sched_current = co;
            co->state = CO_RUNNING;
            /* Flush main-stack callee-saved registers and record the main
             * stack watermark for a possibly-GC-triggering coroutine. */
            jmp_buf switch_flush;
            setjmp(switch_flush);
            _emperor_gc_main_watermark = (char*)&switch_flush;
            _emperor_gc_on_coroutine = 1;
            sched_switch_to_co(co);
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
            } else if (co->state == CO_FD_PARKED) {
                /* Parked on an fd: stays in the fd_waiters list; fd_poll_all
                 * re-queues it when the descriptor turns ready. Re-enqueueing
                 * here (the round-park default) would double-queue it and run
                 * the park loop every round. */
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

        /* External fd sources: probe ready descriptors without blocking so
         * fd events are never starved by (virtual-time) timer bursts. */
        if (fd_waiters && fd_poll_all(0) > 0) {
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

        /* No progress, no timers, but fd waiters remain: block until the
         * next external event. External deltas keep the program alive —
         * quiescence with external sources pending is NOT an exit (a
         * server parked on its stdin is a legitimate steady state). */
        if (fd_waiters) {
            fd_poll_all(-1);
            continue;
        }

        /* No progress, no timers, no external sources: quiescence detection.
         * Transactions that flowed this round (channel writes, event emits)
         * keep the program alive; an unchanged fingerprint two rounds in a
         * row is a normal exit (without fd waiters, "waiting forever" and
         * "deadlock" are indistinguishable — quiescence is a normal exit,
         * matching BabyPenguin). */
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
