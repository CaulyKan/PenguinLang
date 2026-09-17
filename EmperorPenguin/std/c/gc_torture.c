/* GC torture test for the incremental sorted index (gc.c).
 *
 * Validates, under ASan where available:
 *  - survivors keep their exact contents across many collect() cycles,
 *  - interior pointers (into the middle of a block) root their owner,
 *  - garbage is actually reclaimed (managed bytes drop),
 *  - the recycled-address path (free then re-malloc the same size class)
 *    never leaves a dangling index entry (checksum stays stable),
 *  - explicit collect + threshold-triggered collect both behave.
 *
 * Build (from EmperorPenguin/std/c):
 *   clang -I../include -g -fsanitize=address -o /tmp/gc_torture gc_torture.c gc.c gc_span.c scheduler.c
 * (scheduler.c provides _emperor_gc_main_watermark / _emperor_gc_on_coroutine;
 *  pull core_builtin.c too if the link demands it.)
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "emperor_types.h"
#include "emperor_gc.h"

#define KEPT 5000
#define CHURN 200000

/* gc_span.c introspection (gc_internal.h — kept out of the public header
 * on purpose; the torture TU declares just what it reads). */
extern uint64_t _emperor_gc_gt_stats(int which);

typedef struct Node {
    const void* meta;     /* runtime contract: offset 0 is metadata-or-NULL
                           * (NULL here — the precise marker reads it on live
                           * objects; a Node* would be misread as metadata) */
    struct Node* next;    /* kept chain */
    uint64_t payload[7];  /* checksummed contents */
} Node;

/* ---- Precise ref-map walk validation ----
 * Hand-built layouts mirroring real emitter output, exercising every node
 * kind of the emperor_types.h encoding through the collector:
 *
 *   TInner { ptr meta, ptr name }                     M_INNER: one slot @8
 *   TOuter { ptr meta, TInner in @8, i64 tag @24, ptr s @32 }
 *         M_OUTER: inline nested node + bare slot — the poison test stores a
 *         dead block's address in `tag`, which the map does NOT declare: a
 *         precise walk must ignore it (conservative body scan would retain).
 *   TShape { ptr meta, i64 tag @8, payload[24] @16 }  enum with three
 *         variants — none / bare-ptr payload / inline TInner payload — whose
 *         per-tag dispatch is checked twice: a stale pointer sitting in a
 *         no-payload variant's bytes must NOT root its target, and flipping
 *         a live enum's tag to "none" must let the payload target die.
 *
 * The managed-bytes DELTA across a collect is asserted exactly: only the
 * explicitly live graph survives. Any over-retention (poison, stale payload,
 * stack noise from this section's locals — scrubbed before collecting) shows
 * up as extra bytes and fails the test. */
typedef struct TInner {
    const void* meta;
    void* name;
} TInner;

typedef struct TOuter {
    const void* meta;
    TInner in;
    int64_t tag;
    void* s;
} TOuter;

typedef struct TShape {
    const void* meta;
    int64_t tag;
    char payload[24];
} TShape;

/* [total, root-node...]; SLOTS=1, ENUM=2; sub -1 = bare ptr, 0 = none. */
static const int32_t M_INNER[] = {5, 1, 1, 8, -1};
static const int32_t M_OUTER[] = {11, 1, 2, 8, 7, 32, -1, 1, 1, 8, -1};
/* ENUM n=3 (tags 0,1,2): tag1 -> bare-ptr payload node@6, tag2 -> inline
 * TInner payload node@10 (ptr at payload+8). */
static const int32_t M_SHAPE[] = {14, 2, 3, 0, 6, 10, 1, 1, 0, -1, 1, 1, 8, -1};

static const EmperorClassMetadata META_INNER = {"TInner", 16, 0, NULL, NULL, NULL, 0, NULL, NULL, M_INNER};
static const EmperorClassMetadata META_OUTER = {"TOuter", 40, 0, NULL, NULL, NULL, 0, NULL, NULL, M_OUTER};
static const EmperorClassMetadata META_SHAPE = {"TShape", 40, 0, NULL, NULL, NULL, 0, NULL, NULL, M_SHAPE};

/* Zero the dead-frame region below the caller RECURSIVELY: each level's
 * buffer covers the previous level's frame header and alignment slivers
 * (where a single flat memset leaves stale locals — a surviving poison
 * pointer there gets retained by the conservative stack scan and even
 * amplified through the scanner's own spilled loop variables). */
static void scrub_at_depth(int depth) {
    volatile char buf[8192];
    memset((void*)buf, 0, sizeof(buf));
    __asm__ volatile("" ::: "memory");
    if (depth > 0) scrub_at_depth(depth - 1);
}

/* PHYSICALLY zero the callee-saved registers of the CALLING function. A
 * plain helper cannot (callees save/restore them); a clobber-only asm
 * cannot (it changes the compiler's view, not the silicon). Dead poison
 * values parked in rbx/r12-r15 would otherwise be spilled by the
 * collector's setjmp flush onto the scanned stack and pin their targets —
 * retention that depends on frame layout, which is exactly what the exact
 * delta assertions must not depend on. */
#if defined(__x86_64__)
#define CLEAR_CALLEE_SAVED() \
    do { __asm__ volatile( \
        "xorq %%rax, %%rax\n\txorq %%rcx, %%rcx\n\txorq %%rdx, %%rdx\n\t" \
        "xorq %%rsi, %%rsi\n\txorq %%rdi, %%rdi\n\txorq %%r8, %%r8\n\t" \
        "xorq %%r9, %%r9\n\txorq %%r10, %%r10\n\txorq %%r11, %%r11\n\t" \
        "xorq %%rbx, %%rbx\n\txorq %%r12, %%r12\n\txorq %%r13, %%r13\n\t" \
        "xorq %%r14, %%r14\n\txorq %%r15, %%r15" \
        ::: "rax", "rcx", "rdx", "rsi", "rdi", "r8", "r9", "r10", "r11", \
            "rbx", "r12", "r13", "r14", "r15", "memory"); } while (0)
#elif defined(__aarch64__)
#define CLEAR_CALLEE_SAVED() \
    do { __asm__ volatile( \
        "mov x0, xzr\n\tmov x1, xzr\n\tmov x2, xzr\n\tmov x3, xzr\n\t" \
        "mov x4, xzr\n\tmov x5, xzr\n\tmov x6, xzr\n\tmov x7, xzr\n\t" \
        "mov x8, xzr\n\tmov x9, xzr\n\tmov x10, xzr\n\tmov x11, xzr\n\t" \
        "mov x12, xzr\n\tmov x13, xzr\n\tmov x14, xzr\n\tmov x15, xzr\n\t" \
        "mov x16, xzr\n\tmov x17, xzr\n\tmov x18, xzr\n\t" \
        "mov x19, xzr\n\tmov x20, xzr\n\tmov x21, xzr\n\tmov x22, xzr\n\t" \
        "mov x23, xzr\n\tmov x24, xzr\n\tmov x25, xzr\n\tmov x26, xzr\n\t" \
        "mov x27, xzr\n\tmov x28, xzr" \
        ::: "x0", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", \
            "x10", "x11", "x12", "x13", "x14", "x15", "x16", "x17", "x18", \
            "x19", "x20", "x21", "x22", "x23", "x24", "x25", "x26", "x27", \
            "x28", "memory"); } while (0)
#else
#define CLEAR_CALLEE_SAVED() do { } while (0)
#endif

static void scrub(void) {
    scrub_at_depth(4);
}

static void* track_alloc(int size, int is_string, size_t* live_sum) {
    void* p = _emperor_gc_alloc(size, is_string);
    if (!p) { fprintf(stderr, "FAIL: refmap-section alloc(%d) failed\n", size); exit(1); }
    *live_sum += _emperor_gc_alloc_charge(size); /* mode-exact heap charge */
    return p;
}

/* Poison: a dead-only block's address in a NON-declared word (tag), plus
 * a stale pointer to S4 in e2's unscanned payload. Neither may survive.
 * Planted in a helper whose frame (holding the two locals) is dead — and
 * scrubbed — before the collect, so the conservative STACK scan cannot
 * retain the targets either: only precise-walk behavior decides. */
static void plant_poison(TOuter* o, TShape* e2) {
    void* d = _emperor_gc_alloc(777, 0);
    void** s4 = (void**)_emperor_gc_alloc(32, 1);
    o->tag = (int64_t)(uintptr_t)d;
    memcpy(e2->payload, &s4, sizeof(void*));
}

static int refmap_section(void) {
    /* Loose precondition: only the kept chain (KEPT-1 nodes) plus stale-scan
     * slop may be live; the exactness below is measured in DELTAS. */
    /* Normalize first: the churn rounds can leave a few dead string blocks
     * conservatively retained in dead-but-scanned stack slots (retention
     * depends on this build's frame layout) and an emergency auto-collect
     * around the baseline can free them mid-section. Scrub + collect here
     * so `before` measures a steady state — the exact-delta assertion
     * below assumes zero pre-existing stale retention. */
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    uint64_t before = _emperor_gc_info();
    printf("refmap section: baseline managed=%llu\n", (unsigned long long)before);

    size_t live = 0;
    uint64_t magic = 0xC0FFEE00;

    TOuter* o = (TOuter*)track_alloc((int)sizeof(TOuter), 0, &live);
    o->meta = &META_OUTER;
    o->in.meta = &META_INNER;
    o->tag = 0;
    o->s = NULL;
    TInner* i2 = (TInner*)track_alloc((int)sizeof(TInner), 0, &live);
    i2->meta = &META_INNER;
    i2->name = NULL;
    void** s1 = (void**)track_alloc(32, 1, &live);
    void** s2 = (void**)track_alloc(32, 1, &live);
    void** s3 = (void**)track_alloc(32, 1, &live);
    void** s5 = (void**)track_alloc(32, 1, &live);
    s1[0] = (void*)magic;
    s2[0] = (void*)(magic + 1);
    s3[0] = (void*)(magic + 2);
    s5[0] = (void*)(magic + 4);

    TShape* e1 = (TShape*)track_alloc((int)sizeof(TShape), 0, &live);
    e1->meta = &META_SHAPE;
    e1->tag = 1; /* bare-ptr payload variant */
    memcpy(e1->payload, &s3, sizeof(void*));
    TShape* e2 = (TShape*)track_alloc((int)sizeof(TShape), 0, &live);
    e2->meta = &META_SHAPE;
    e2->tag = 0; /* no-payload variant: the S4 bytes below must be ignored */
    e2->payload[0] = (char)0xAA;

    /* Poison: a dead-only block's address in a NON-declared word (tag), plus
     * a stale pointer to S4 in e2's unscanned payload. Neither may survive. */
    plant_poison(o, e2);

    /* Publish the graph through rooted handles, scrub, collect. */
    _emperor_gc_add_root((void**)&o);
    _emperor_gc_add_root((void**)&i2);
    _emperor_gc_add_root((void**)&e1);
    _emperor_gc_add_root((void**)&e2);
    o->in.name = s1;
    o->s = s2;
    i2->name = s5;

    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();

    uint64_t after = _emperor_gc_info();
    if (after != before + live) {
        uint64_t ob = 0, yb = 0;
        _emperor_gc_info_split(&ob, &yb);
        fprintf(stderr, "FAIL: precise delta %llu != live graph %zu "
                "(poison/stale retention: conservative leak?) "
                "[old=%llu young=%llu before=%llu]\n",
                (unsigned long long)(after - before), live,
                (unsigned long long)ob, (unsigned long long)yb,
                (unsigned long long)before);
        return 1;
    }
    if (s1[0] != (void*)magic || s2[0] != (void*)(magic + 1) ||
        s3[0] != (void*)(magic + 2) || s5[0] != (void*)(magic + 4)) {
        fprintf(stderr, "FAIL: refmap graph target corrupted\n");
        return 1;
    }
    printf("refmap section: precise delta exact (%zu bytes live, poison ignored)\n", live);

    /* Tag flip: e1 goes from the ptr-payload variant to "none" while its
     * payload bytes still hold S3 — S3 must now be collected. The s3 LOCAL
     * slot must be cleared too: the still-conservative stack scan would
     * otherwise retain its target regardless of the walk's precision. */
    uint64_t before2 = _emperor_gc_info();
    e1->tag = 0;
    s3 = NULL;
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    uint64_t after2 = _emperor_gc_info();
    size_t s3_total = _emperor_gc_alloc_charge(32);
    if (after2 != before2 - s3_total) {
        fprintf(stderr, "FAIL: stale payload retained after tag flip "
                "(delta %lld, want -%zu)\n",
                (long long)(after2 - before2), s3_total);
        return 1;
    }
    printf("refmap section: enum tag flip frees stale payload target\n");
    /* The rooted handles are STACK locals — unregister before returning or
     * the next collect (the generational section) dereferences a dead
     * frame (ASan: stack-use-after-return; a minor would REWRITE it). */
    _emperor_gc_remove_root((void**)&o);
    _emperor_gc_remove_root((void**)&i2);
    _emperor_gc_remove_root((void**)&e1);
    _emperor_gc_remove_root((void**)&e2);
    return 0;
}


/* G8 — AUTO minor under load: no explicit collect (no conservative main-
 * stack cover), 16MB of young churn across ~256 chunks, survivors rooted
 * through precise global roots only. This is the shape the fd-parked md
 * test exercises: bug shows only at default budget scale, not at tiny
 * budgets (reproduced: EMPEROR_GC_YOUNG=64K hides it). */




static Node* kept_head = NULL;

static uint64_t kept_checksum(void) {
    uint64_t s = 0;
    for (Node* n = kept_head; n; n = n->next)
        for (int i = 0; i < 7; i++) s = s * 31 + n->payload[i];
    return s;
}

/* ---- Generational section (GC v2 phase 3b) ----
 * Only meaningful under EMPEROR_GC_MODE=precise (the nursery). Handles live
 * in GLOBAL roots and file statics (never live-stack locals): the explicit
 * collect's conservative cover would pin anything the active frames still
 * reference, and these tests assert MOVEMENT. */

static int g_dispose_ran = 0;

static void torture_dispose(void* p) { (void)p; g_dispose_ran++; }

static const EmperorClassMetadata META_DISPOSE = {
    "TDispose", 40, 0, NULL, NULL, NULL, 0, NULL, torture_dispose, NULL};

/* Every case's SETUP runs in its own function: its frame dies on return and
 * scrub() erases the register/stack residue of the young pointers, so the
 * explicit collect's conservative cover cannot pin the objects these tests
 * assert MOVEMENT for (a live-frame leftover would legitimately pin). */










/* ---- Green-tea span heap section (GC v3) ----
 * EMPEROR_GC_MODE=greentea only. Covers the span/malloc-path split at the
 * 512B slot boundary: a mixed small<->large graph (span slot <-> malloc
 * block, both directions) must survive churn + explicit collects byte-exact
 * (M1's per-object marking resolves through BOTH heaps); dead span slots
 * run their finalizers exactly once; and dropping the graph returns
 * gc_info EXACTLY to the pre-section baseline (dead spans reclaimed
 * wholesale, no slot leakage). Exact deltas hold because span slots are
 * charged by size class via _emperor_gc_alloc_charge. */
typedef struct GtSmallNode {
    const void* meta;         /* NULL: conservative body scan, like Node */
    struct GtBigNode* big;    /* span -> malloc edge (600B body: large)   */
    uint64_t v[2];            /* 32B body -> 64B slot                    */
} GtSmallNode;
typedef struct GtBigNode {
    const void* meta;
    struct GtSmallNode* back; /* malloc -> span edge                     */
    uint64_t v[73];           /* 592B body -> malloc (large) path        */
} GtBigNode;

static int gt_dispose_ran = 0;
typedef struct BnNode {
    const void* meta;         /* NULL: conservative body scan, like Node */
    struct BnNode* next;
    uint64_t v[6];            /* 64B body */
} BnNode;

static uint64_t bench_rng = 0x9E3779B97F4A7C15ULL;
static uint64_t bench_next(void) {
    bench_rng ^= bench_rng << 13; bench_rng ^= bench_rng >> 7; bench_rng ^= bench_rng << 17;
    return bench_rng;
}

static void gt_dispose(void* p) { (void)p; gt_dispose_ran++; }
static const EmperorClassMetadata META_GT_DISPOSE = {
    "GtDispose", 40, 0, NULL, NULL, NULL, 0, NULL, gt_dispose, NULL};

static int greentea_section(void) {
    const char* mode = getenv("EMPEROR_GC_MODE");
    int gt = mode == NULL || mode[0] == '\0' ||
             strcmp(mode, "greentea") == 0 || strcmp(mode, "default") == 0;
    if (!gt) {
        printf("greentea section: skipped (not in greentea mode)\n");
        return 0;
    }

    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    uint64_t before = _emperor_gc_info();

    /* Mixed-graph integrity across churn + explicit collects. */
    GtSmallNode* sm = (GtSmallNode*)_emperor_gc_alloc((int)sizeof(GtSmallNode), 0);
    GtBigNode* bg = (GtBigNode*)_emperor_gc_alloc((int)sizeof(GtBigNode), 0);
    if (!sm || !bg) { fprintf(stderr, "FAIL gt: mixed-graph alloc failed\n"); return 1; }
    char* st = (char*)_emperor_gc_alloc(24, 1);
    sm->meta = NULL; sm->big = bg; sm->v[0] = 0xAA112233445566ULL; sm->v[1] = 2;
    bg->meta = NULL; bg->back = sm; bg->v[0] = 0xBB778899AABBCCULL;
    st[0] = 'g'; st[1] = '\0';
    /* Root via the registry like the generational section's statics: a
     * stack-local handle is only covered by the conservative scan up to
     * _emperor_gc_init's anchor — locals above it are legitimately
     * uncoverable (the emitted main passes llvm.frameaddress, the frame
     * top; this C harness passes an arbitrary local). */
    _emperor_gc_add_root((void**)&sm);
    _emperor_gc_add_root((void**)&st);
    for (int i = 0; i < 200000; i++) {
        void* junk = _emperor_gc_alloc(24 + (i & 63), 0); /* 48..80B slots */
        ((volatile char*)junk)[16] = 1;
        _emperor_gc_poll();
        if ((i & 16383) == 0) _emperor_gc_collect();
    }
    if (!sm->big || sm->big != bg || sm->big->back != sm ||
        sm->v[0] != 0xAA112233445566ULL || sm->v[1] != 2 ||
        sm->big->v[0] != 0xBB778899AABBCCULL || st[0] != 'g' || st[1] != '\0') {
        fprintf(stderr, "FAIL gt: mixed span/malloc graph corrupted\n");
        return 1;
    }

    /* Finalizers: dead span slots run dispose exactly once each. */
    gt_dispose_ran = 0;
    for (int i = 0; i < 5000; i++) {
        void* d = _emperor_gc_alloc(40, 0);
        *(EmperorClassMetadata**)d = (EmperorClassMetadata*)&META_GT_DISPOSE;
    }
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    if (gt_dispose_ran != 5000) {
        fprintf(stderr, "FAIL gt: finalizer count %d != 5000\n", gt_dispose_ran);
        return 1;
    }

    /* Queue wrap+grow regression: PAIRED graph — nodes are allocated in
     * adjacent PAIRS (pair-mates share a span), and every node's four
     * edges target two OTHER pairs (both mates of each): scanning any
     * object grays TWO DISTINCT slots of each target span at once, so the
     * first gray pends a representative and the second immediately flips
     * REP_HIT and ENQUEUES — guaranteed queue growth of +2 spans per
     * object scanned. A small rooted seed lets the drain's BFS frontier
     * outrun consumption; the ring crosses its cap mid-drain (wrap,
     * head > 0) and the next burst GROWS the queue mid-wrap. The pre-fix
     * grow orphaned the wrapped prefix (cap doubling changed the ring
     * modulus) and dequeue walked uninitialized slots — the pass3
     * self-compile SIGSEGV under the greentea default. Sparse or
     * group-crossing random edges do NOT fire this (the representative
     * path dominates and consumption keeps pace — measured: queue never
     * passed its first 256 growth). */
    {
        enum { WPAIRS = 100000, WSEED = 64 };
        static void** wseed; /* typed buffer seeding the first pairs */
        wseed = (void**)calloc(WSEED, sizeof(void*));
        _emperor_gc_track_buffer(wseed, WSEED, 8, _emperor_gc_bare_refmap);
        BnNode** all = (BnNode**)malloc((size_t)WPAIRS * 2 * sizeof(BnNode*));
        for (int i = 0; i < WPAIRS * 2; i++) {
            /* 88B body: word0 metadata(NULL), word1 stamp, words2..5 edges,
             * words6..10 dead weight (span padding). Slot 112B. */
            BnNode* n = (BnNode*)_emperor_gc_alloc(88, 0);
            ((uint64_t*)n)[1] = (uint64_t)(i + 1);
            all[i] = n;
        }
        for (int p = 0; p < WPAIRS; p++) {
            uint64_t** a = (uint64_t**)all[p * 2];
            uint64_t** b = (uint64_t**)all[p * 2 + 1];
            int ta = (int)(bench_next() % WPAIRS);
            int tb = (int)(bench_next() % WPAIRS);
            a[2] = (uint64_t*)all[ta * 2];
            a[3] = (uint64_t*)all[ta * 2 + 1];
            a[4] = (uint64_t*)all[tb * 2];
            a[5] = (uint64_t*)all[tb * 2 + 1];
            b[2] = (uint64_t*)all[ta * 2 + 1];
            b[3] = (uint64_t*)all[ta * 2];
            b[4] = (uint64_t*)all[tb * 2 + 1];
            b[5] = (uint64_t*)all[tb * 2];
        }
        for (int s = 0; s < WSEED; s++) wseed[s] = all[s];
        for (int rep = 0; rep < 3; rep++) {
            _emperor_gc_collect();
            for (int s = 0; s < WSEED; s += 2) {
                if (!wseed[s] ||
                    ((uint64_t*)wseed[s])[1] != (uint64_t)(s + 1)) {
                    fprintf(stderr, "FAIL gt: wrap-grow seed %d freed/corrupted\n", s);
                    return 1;
                }
            }
        }
        _emperor_gc_untrack_buffer(wseed);
        free(wseed);
        free(all);
    }

    /* Wholesale reclaim: drop the graph, exact return to baseline. */
    _emperor_gc_remove_root((void**)&sm);
    _emperor_gc_remove_root((void**)&st);
    sm = NULL; bg = NULL; st = NULL; /* locals: the conservative cover reads them */
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    uint64_t after = _emperor_gc_info();
    if (after != before) {
        fprintf(stderr, "FAIL gt: post-drop managed %llu != baseline %llu "
                "(span slot leak?)\n",
                (unsigned long long)after, (unsigned long long)before);
        return 1;
    }
    printf("greentea section: span/malloc graph, finalizers, wholesale reclaim all pass\n");
    printf("greentea stats: cycles=%llu wholesale=%llu rep=%llu live=%llu goal=%llu "
           "full=%llu partial=%llu empty=%llu supers=%llu\n",
           (unsigned long long)_emperor_gc_gt_stats(7),
           (unsigned long long)_emperor_gc_gt_stats(5),
           (unsigned long long)_emperor_gc_gt_stats(6),
           (unsigned long long)_emperor_gc_gt_stats(0),
           (unsigned long long)_emperor_gc_gt_stats(1),
           (unsigned long long)_emperor_gc_gt_stats(2),
           (unsigned long long)_emperor_gc_gt_stats(3),
           (unsigned long long)_emperor_gc_gt_stats(4),
           (unsigned long long)_emperor_gc_gt_stats(8));
    return 0;
}

/* ---- Benchmark workloads (driven by `make gc-bench` -> gc_bench.sh) ----
 * `gc_torture bench <workload>` runs exactly one deterministic workload and
 * exits; a no-arg invocation keeps the correctness sections untouched. With
 * GC_PROFILE=1 in the environment the runtime prints a single-line
 * machine-parseable phase summary (gc.c: _gc_st_report) at exit, which
 * gc_bench.sh tabulates per collector mode. The *_WORKLOADS list at the end
 * is duplicated in gc_bench.sh — keep them in sync.
 *
 *   churn       — infant-mortality churn (the v2 nursery's home turf)
 *   ptrdense    — pointer-dense: persistent linked graph + relink churn
 *   container   — typed-region (container buffer) element churn
 *   deepsurvive — deep survival: a growing persistent forest + churn
 *   finstorm    — finalizer storm: destructor-stamped objects dying in waves
 *   mixed512    — bodies straddling the 512B span cutoff (200..600)
 */
static BnNode* pd_head = NULL;
static BnNode* ds_chains[64];
static void* mx_ring[32];

static int bench_churn(void) {
    for (int i = 0; i < 4000000; i++) {
        void* junk = _emperor_gc_alloc(24 + (int)(bench_next() & 24), 0);
        ((volatile char*)junk)[8] = (char)i;
        if ((i & 3) == 0) {
            char* s = (char*)_emperor_gc_alloc(24, 1);
            s[0] = 'z'; s[1] = '\0';
        }
        _emperor_gc_poll();
        if ((i & 262143) == 0) _emperor_gc_collect();
    }
    printf("bench churn done\n");
    return 0;
}

static int bench_ptrdense(void) {
    _emperor_gc_add_root((void**)&pd_head);
    for (int i = 0; i < 30000; i++) {
        BnNode* n = (BnNode*)_emperor_gc_alloc((int)sizeof(BnNode), 0);
        for (int k = 0; k < 6; k++) n->v[k] = bench_next();
        n->next = pd_head;
        pd_head = n;
    }
    uint64_t sum = 0;
    for (int i = 0; i < 2000000; i++) {
        void* junk = _emperor_gc_alloc(24 + (int)(bench_next() & 16), 0);
        ((volatile char*)junk)[8] = 1;
        /* relink one near node to the head: pointer-dense rewrites */
        if (pd_head && (i & 255) == 0) {
            BnNode* n = pd_head;
            int step = (int)(bench_next() & 63);
            while (n->next && step--) n = n->next;
            BnNode* nn = (BnNode*)_emperor_gc_alloc((int)sizeof(BnNode), 0);
            nn->v[0] = n->v[0];
            nn->next = pd_head;
            pd_head = nn;
            sum += nn->v[0];
        }
        _emperor_gc_poll();
        if ((i & 131071) == 0) _emperor_gc_collect();
    }
    uint64_t got = 0;
    for (BnNode* w = pd_head; w; w = w->next) got ^= w->v[0];
    (void)got; (void)sum;
    printf("bench ptrdense done\n");
    return 0;
}

#define CT_BUFS 32
#define CT_ELEMS 64
static int bench_container(void) {
    void*** bufs = (void***)malloc(CT_BUFS * sizeof(void**));
    if (!bufs) return 1;
    for (int b = 0; b < CT_BUFS; b++) {
        bufs[b] = (void**)calloc(CT_ELEMS, sizeof(void*));
        if (!bufs[b]) return 1;
        _emperor_gc_track_buffer(bufs[b], CT_ELEMS, 8, _emperor_gc_bare_refmap);
    }
    for (int i = 0; i < 1500000; i++) {
        int b = (int)(bench_next() % CT_BUFS);
        int e = (int)(bench_next() % CT_ELEMS);
        char* s = (char*)_emperor_gc_alloc(24 + (int)(bench_next() & 24), 1);
        s[0] = (char)('a' + (i & 25)); s[1] = '\0';
        bufs[b][e] = s;
        _emperor_gc_poll();
        if ((i & 131071) == 0) _emperor_gc_collect();
    }
    for (int b = 0; b < CT_BUFS; b++) {
        _emperor_gc_untrack_buffer(bufs[b]);
        free(bufs[b]);
    }
    free(bufs);
    printf("bench container done\n");
    return 0;
}

#define DS_CHAINS 64
static int bench_deepsurvive(void) {
    for (int c = 0; c < DS_CHAINS; c++)
        _emperor_gc_add_root((void**)&ds_chains[c]);
    for (int b = 0; b < 160; b++) {
        for (int c = 0; c < DS_CHAINS; c++) {
            for (int d = 0; d < 64; d++) {
                BnNode* n = (BnNode*)_emperor_gc_alloc((int)sizeof(BnNode), 0);
                n->v[0] = bench_next();
                n->next = ds_chains[c];
                ds_chains[c] = n;
            }
        }
        for (int i = 0; i < 20000; i++) {
            void* junk = _emperor_gc_alloc(24 + (int)(bench_next() & 24), 0);
            ((volatile char*)junk)[8] = 1;
            _emperor_gc_poll();
        }
        _emperor_gc_collect();
    }
    uint64_t chk = 0;
    for (int c = 0; c < DS_CHAINS; c++)
        for (BnNode* w = ds_chains[c]; w; w = w->next) chk ^= w->v[0];
    (void)chk;
    printf("bench deepsurvive done\n");
    return 0;
}

static int bench_finstorm(void) {
    for (int r = 0; r < 60; r++) {
        for (int i = 0; i < 20000; i++) {
            void* d = _emperor_gc_alloc(40, 0);
            *(EmperorClassMetadata**)d = (EmperorClassMetadata*)&META_DISPOSE;
            _emperor_gc_poll();
        }
        _emperor_gc_collect();
        if (g_dispose_ran == 0) {
            fprintf(stderr, "bench finstorm: no finalizers ran\n");
            return 1;
        }
    }
    printf("bench finstorm done\n");
    return 0;
}

static int bench_mixed512(void) {
    for (int r = 0; r < 32; r++) _emperor_gc_add_root((void**)&mx_ring[r]);
    for (int i = 0; i < 1000000; i++) {
        int sz = 200 + (int)(bench_next() % 401);   /* body 200..600 */
        void* p = _emperor_gc_alloc(sz, 0);
        /* runtime contract: body word 0 is the metadata slot — scribble
         * only the payload (see the existing churn section's note). */
        ((volatile char*)p)[16] = 1;
        mx_ring[bench_next() & 31] = p;
        _emperor_gc_poll();
        if ((i & 131071) == 0) _emperor_gc_collect();
    }
    printf("bench mixed512 done\n");
    return 0;
}

static const char* _WORKLOADS[] = {
    "churn", "ptrdense", "container", "deepsurvive", "finstorm", "mixed512", NULL
};

static int bench_dispatch(const char* name) {
    if (strcmp(name, "churn") == 0) return bench_churn();
    if (strcmp(name, "ptrdense") == 0) return bench_ptrdense();
    if (strcmp(name, "container") == 0) return bench_container();
    if (strcmp(name, "deepsurvive") == 0) return bench_deepsurvive();
    if (strcmp(name, "finstorm") == 0) return bench_finstorm();
    if (strcmp(name, "mixed512") == 0) return bench_mixed512();
    fprintf(stderr, "unknown workload '%s' (valid:", name);
    for (int i = 0; _WORKLOADS[i]; i++) fprintf(stderr, " %s", _WORKLOADS[i]);
    fprintf(stderr, ")\n");
    return 2;
}

int main(int argc, char** argv) {
    if (argc >= 2 && strcmp(argv[1], "bench") == 0) {
        if (argc < 3) {
            fprintf(stderr, "usage: gc_torture bench <workload>\n");
            for (int i = 0; _WORKLOADS[i]; i++) fprintf(stderr, "  %s\n", _WORKLOADS[i]);
            return 2;
        }
        int stack_anchor = 0;
        _emperor_gc_init(&stack_anchor);
        return bench_dispatch(argv[2]);
    }
    int stack_anchor = 0;
    _emperor_gc_init(&stack_anchor);
    _emperor_gc_add_root((void**)&kept_head);

    /* Build the kept chain with deterministic contents. */
    uint64_t seed = 88172645463325252ULL;
    for (int i = 0; i < KEPT; i++) {
        Node* n = (Node*)_emperor_gc_alloc((int)sizeof(Node), 0);
        if (!n) { fprintf(stderr, "alloc kept failed\n"); return 1; }
        for (int k = 0; k < 7; k++) {
            seed ^= seed << 13; seed ^= seed >> 7; seed ^= seed << 17;
            n->payload[k] = seed;
        }
        n->next = kept_head;
        kept_head = n;
    }
    uint64_t want = kept_checksum();
    printf("kept checksum: %llu, managed=%llu bytes\n",
           (unsigned long long)want, (unsigned long long)_emperor_gc_info());

    /* Interior-pointer check: a malloc'd (untracked) buffer holding pointers
     * into the MIDDLE of kept nodes, registered as a scan region. */
    Node** mids = (Node**)malloc(64 * sizeof(Node*));
    Node* walk = kept_head;
    for (int i = 0; i < 64; i++) {
        mids[i] = (Node*)((char*)walk + 16); /* interior: &payload[0] region */
        walk = walk->next;
    }
    _emperor_gc_scan_add(mids, 64 * sizeof(Node*));

    /* Churn: garbage waves between collections, with address-recycling
     * pressure (same size class as the kept nodes). Interior pointers are
     * probed every round: reading through them after collect catches a
     * wrongly-freed owner (ASan) or a corrupted index. */
    uint64_t mids_want = 0;
    for (int i = 0; i < 64; i++)
        mids_want = mids_want * 31 + ((uint64_t*)mids[i])[0];
    for (int round = 0; round < 8; round++) {
        for (int i = 0; i < CHURN; i++) {
            Node* junk = (Node*)_emperor_gc_alloc((int)sizeof(Node), 0);
            junk->payload[0] = (uint64_t)i;
            /* string blocks too (is_string=1 path) */
            char* s = (char*)_emperor_gc_alloc(24, 1);
            s[0] = 'x'; s[23] = '\0';
        }
        _emperor_gc_collect();
        uint64_t got = kept_checksum();
        if (got != want) {
            fprintf(stderr, "FAIL round %d: checksum %llu != %llu\n",
                    round, (unsigned long long)got, (unsigned long long)want);
            return 1;
        }
        uint64_t mids_got = 0;
        for (int i = 0; i < 64; i++)
            mids_got = mids_got * 31 + ((uint64_t*)mids[i])[0];
        if (mids_got != mids_want) {
            fprintf(stderr, "FAIL round %d: interior-pointer targets corrupted\n", round);
            return 1;
        }
    }

    uint64_t final_managed = _emperor_gc_info();
    printf("after churn: managed=%llu bytes (kept-only expected << churn total)\n",
           (unsigned long long)final_managed);
    if (final_managed > (uint64_t)KEPT * (sizeof(Node) + 64) * 4) {
        fprintf(stderr, "FAIL: garbage not reclaimed\n");
        return 1;
    }

    /* Drop the chain head; collect; ensure the rest of the chain survives
     * intact (partial-survival / index-compaction probe). The dropped node's
     * offset-0 word is cleared first: non-string blocks carry an
     * EmperorClassMetadata* (or NULL) at offset 0 by runtime contract —
     * leaving a Node* there would make the finalizer read it as metadata. */
    Node* new_head = (Node*)kept_head->next;
    uint64_t head0 = new_head->payload[0];
    ((Node*)kept_head)->next = NULL;
    kept_head = new_head;
    _emperor_gc_scan_remove(mids);
    free(mids);
    for (int r = 0; r < 3; r++) _emperor_gc_collect();
    if (kept_head->payload[0] != head0) {
        fprintf(stderr, "FAIL: survivor corrupted\n");
        return 1;
    }

    if (refmap_section()) return 1;
    if (greentea_section()) return 1;

    printf("PASS: survivors intact, garbage reclaimed, index stable, ref-maps precise\n");
    return 0;
}
