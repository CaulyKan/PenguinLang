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
 *   clang -I../include -g -fsanitize=address -o /tmp/gc_torture gc_torture.c gc.c scheduler.c
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
    *live_sum += sizeof(void*) == 8 ? 24 + size : 0; /* GCHeader + body */
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
    size_t s3_total = 24 + 32;
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
#define G8_KEPT 128
static TOuter* g8_kept[G8_KEPT];

static void setup_g8(void) {
    for (int i = 0; i < G8_KEPT; i++) {
        TOuter* o = (TOuter*)_emperor_gc_alloc((int)sizeof(TOuter), 0);
        o->meta = &META_OUTER;
        o->in.meta = &META_INNER;
        o->in.name = NULL;
        o->tag = 1000 + i;
        o->s = _emperor_gc_alloc(32, 1);
        ((char*)o->s)[0] = (char)('a' + (i % 26));
        g8_kept[i] = o;
    }
}

static int g8_check(void) {
    for (int i = 0; i < G8_KEPT; i++) {
        TOuter* o = g8_kept[i];
        if (!o || o->tag != 1000 + i || !o->s || ((char*)o->s)[0] != (char)('a' + (i % 26))) {
            fprintf(stderr, "FAIL G8: kept[%d] corrupted (tag=%lld)\n",
                    i, o ? (long long)o->tag : -1);
            return 1;
        }
    }
    return 0;
}

static int auto_minor_section(void) {
    const char* mode = getenv("EMPEROR_GC_MODE");
    const char* nogen = getenv("EMPEROR_GC_NOGEN");
    int gen = mode != NULL && strcmp(mode, "precise") == 0 &&
              !(nogen && nogen[0] && nogen[0] != '0');
    if (!gen) return 0;
    for (int round = 0; round < 3; round++) {
        setup_g8();
        /* churn past the young budget; the poll drives the AUTO minor */
        for (int i = 0; i < 200000; i++) {
            void* junk = _emperor_gc_alloc(48, 0);
            /* runtime contract: offset 0 is the metadata slot (NULL here);
             * scribble only the payload — pinning a conservatively-seen
             * half-initialized object must be survivable, but a forged
             * metadata word is out of contract. */
            ((char*)junk)[16] = 1;
            _emperor_gc_poll();
        }
        if (g8_check()) return 1;
        for (int i = 0; i < G8_KEPT; i++) g8_kept[i] = NULL;
        scrub();
        CLEAR_CALLEE_SAVED();
        _emperor_gc_collect();
    }
    printf("auto-minor section: %d kept survive %d rounds at default budget\n",
           G8_KEPT, 3);
    return 0;
}

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

static TOuter* g_hold;
static TOuter* g_old_h;
static void* g_old_s;
static void* g_old_in;
static void* g_big;
static void** g_pinbuf;
static void** g_tbuf;
static int g_dispose_ran = 0;

static void torture_dispose(void* p) { (void)p; g_dispose_ran++; }

static const EmperorClassMetadata META_DISPOSE = {
    "TDispose", 40, 0, NULL, NULL, NULL, 0, NULL, torture_dispose, NULL};

/* Every case's SETUP runs in its own function: its frame dies on return and
 * scrub() erases the register/stack residue of the young pointers, so the
 * explicit collect's conservative cover cannot pin the objects these tests
 * assert MOVEMENT for (a live-frame leftover would legitimately pin). */

static void setup_g1(void) {
    TOuter* h = (TOuter*)_emperor_gc_alloc((int)sizeof(TOuter), 0);
    h->meta = &META_OUTER;
    h->in.meta = &META_INNER;
    h->in.name = NULL;
    h->tag = 42;
    h->s = NULL;
    char* s1 = (char*)_emperor_gc_alloc(32, 1);
    s1[0] = 'A'; s1[1] = '\0';
    h->s = s1;
    g_hold = h;
    g_old_h = h;
    g_old_s = s1;
}

static void setup_g2(void) {
    char* s2 = (char*)_emperor_gc_alloc(32, 1);
    s2[0] = 'Z'; s2[1] = '\0';
    g_hold->s = s2; /* old object's field store of a young value */
    _emperor_gc_write_barrier(g_hold, &g_hold->s);
    g_old_s = s2;
}

static void setup_g3(void) {
    TInner in2;
    in2.meta = &META_INNER;
    char* s3 = (char*)_emperor_gc_alloc(32, 1);
    s3[0] = 'M'; s3[1] = '\0';
    in2.name = s3;
    g_hold->in = in2; /* struct store: embedded ref lands in an old object */
    _emperor_gc_write_barrier_map(g_hold, &g_hold->in, M_INNER);
    g_old_s = s3;
}

static void setup_g4(void) {
    void* d = _emperor_gc_alloc(40, 0);
    *(EmperorClassMetadata**)d = (EmperorClassMetadata*)&META_DISPOSE;
    g_dispose_ran = 0;
}

static void setup_g5(void) {
    g_pinbuf = (void**)malloc(8 * sizeof(void*));
    char* s5 = (char*)_emperor_gc_alloc(32, 1);
    s5[0] = 'P'; s5[1] = '\0';
    g_pinbuf[0] = s5;
    _emperor_gc_scan_add(g_pinbuf, 8 * sizeof(void*));
    g_old_s = s5;
}

static void setup_g6(void) {
    g_tbuf = (void**)malloc(2 * sizeof(void*));
    char* s6 = (char*)_emperor_gc_alloc(32, 1);
    s6[0] = 'T'; s6[1] = '\0';
    /* Registration BEFORE any element lands: _emperor_gc_track_buffer
     * ZEROES the buffer (the 238ad821 malloc-residue guard), so storing
     * first would erase the element — mirror the container contract
     * (List._grow / vector / hashmap all register, then fill). */
    _emperor_gc_track_buffer(g_tbuf, 2, 8, _emperor_gc_bare_refmap);
    g_tbuf[0] = s6;
    g_tbuf[1] = NULL;
    g_old_s = s6;
}

/* G9 — CARD-TABLE granularity: the barrier'd store dirties the 512B card
 * of the slot, and the minor scans the WHOLE overlapping object — so a
 * young value written into a DIFFERENT slot of the same old object with
 * NO barrier at all (deliberate: this models a missed/omitted barrier) is
 * still rescued by the shared card. A slot-precise remembered set would
 * miss it; do not "fix" the unbarriered store away. The bare slot is also
 * stored twice (young value overwritten by a younger one) — the scan must
 * judge the CURRENT field value. */
static void setup_g9(void) {
    char* na = (char*)_emperor_gc_alloc(32, 1);
    na[0] = 'N'; na[1] = '\0';
    g_hold->in.name = na; /* NO barrier — rescued via the shared card only */
    g_old_in = na;
    char* dead = (char*)_emperor_gc_alloc(32, 1);
    dead[0] = 'D'; dead[1] = '\0';
    g_hold->s = dead; /* barrier'd ... */
    _emperor_gc_write_barrier(g_hold, &g_hold->s);
    char* live = (char*)_emperor_gc_alloc(32, 1);
    live[0] = 'W'; live[1] = '\0';
    g_hold->s = live; /* ... then overwritten before the collect */
    _emperor_gc_write_barrier(g_hold, &g_hold->s);
    g_old_s = live;
}

/* G10 — a barrier'd young store marks the card, then the slot is dropped
 * to NULL WITHOUT a barrier (a NULL store carries no edge): the dirty
 * card's scan must leave the NULL alone — no resurrect, no crash trying
 * to resolve the now-dead value through memory the minor recycles. */
static void setup_g10(void) {
    char* s10 = (char*)_emperor_gc_alloc(32, 1);
    s10[0] = 'X'; s10[1] = '\0';
    g_hold->s = s10;
    _emperor_gc_write_barrier(g_hold, &g_hold->s);
    g_hold->s = NULL;
    g_old_s = NULL;
}

static int generational_section(void) {
    const char* mode = getenv("EMPEROR_GC_MODE");
    const char* nogen = getenv("EMPEROR_GC_NOGEN");
    int gen = mode != NULL && strcmp(mode, "precise") == 0 &&
              !(nogen && nogen[0] && nogen[0] != '0');
    if (!gen) {
        printf("generational section: skipped (not in generational mode)\n");
        return 0;
    }

    /* Normalize to a quiet heap first. */
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();

    /* G1 — promotion moves survivors, rewrites rooted handles AND the
     * references held inside promoted objects (the forwarding machinery). */
    setup_g1();
    scrub();
    CLEAR_CALLEE_SAVED();
    uint64_t pins0 = _emperor_gc_debug_pin_count();
    _emperor_gc_collect();
    if (g_hold == g_old_h) {
        if (_emperor_gc_debug_pin_count() == pins0) {
            fprintf(stderr, "FAIL G1: holder neither promoted nor pinned\n");
            return 1;
        }
        /* pinned at its address: valid conservative outcome */
    }
    if (g_hold->tag != 42 || g_hold->s == NULL) {
        fprintf(stderr, "FAIL G1: promoted holder corrupted\n");
        return 1;
    }
    if (g_hold->s == g_old_s) {
        fprintf(stderr, "FAIL G1: indirect child slot not rewritten\n");
        return 1;
    }
    if (((char*)g_hold->s)[0] != 'A') {
        fprintf(stderr, "FAIL G1: child payload corrupted across promotion\n");
        return 1;
    }

    /* G2 — bare write barrier: an OLD object's field store of a YOUNG value
     * must survive the next minor with the slot rewritten. */
    setup_g2();
    scrub();
    CLEAR_CALLEE_SAVED();
    pins0 = _emperor_gc_debug_pin_count();
    _emperor_gc_collect();
    if (g_hold->s == g_old_s) {
        if (_emperor_gc_debug_pin_count() == pins0) {
            fprintf(stderr, "FAIL G2: barrier'd child neither promoted nor pinned\n");
            return 1;
        }
    }
    if (((char*)g_hold->s)[0] != 'Z') {
        fprintf(stderr, "FAIL G2: barrier'd child corrupted\n");
        return 1;
    }

    /* G3 — map write barrier: whole-struct store embedding a reference. */
    setup_g3();
    scrub();
    CLEAR_CALLEE_SAVED();
    pins0 = _emperor_gc_debug_pin_count();
    _emperor_gc_collect();
    if (g_hold->in.name == g_old_s) {
        if (_emperor_gc_debug_pin_count() == pins0) {
            fprintf(stderr, "FAIL G3: embedded ref neither promoted nor pinned "
                    "[hold=%p in.name=%p old_s=%p]\n",
                    (void*)g_hold, g_hold->in.name, g_old_s);
            return 1;
        }
    }
    if (((char*)g_hold->in.name)[0] != 'M') {
        fprintf(stderr, "FAIL G3: embedded child corrupted\n");
        return 1;
    }

    /* G4 — young-generation finalizer: a dead young object with a class
     * destructor runs dispose_mem at the MINOR (before chunks recycle). */
    setup_g4();
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    if (g_dispose_ran == 0) {
        fprintf(stderr, "FAIL G4: young dead object's finalizer never ran\n");
        return 1;
    }

    /* G5 — conservative pin: a young object referenced ONLY by a raw
     * registered buffer keeps its ADDRESS across a minor. */
    setup_g5();
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    if (g_pinbuf[0] != g_old_s) {
        fprintf(stderr, "FAIL G5: conservatively-held young object MOVED\n");
        return 1;
    }
    if (((char*)g_pinbuf[0])[0] != 'P') {
        fprintf(stderr, "FAIL G5: pinned object corrupted\n");
        return 1;
    }
    _emperor_gc_scan_remove(g_pinbuf);
    g_pinbuf[0] = NULL;
    free(g_pinbuf);
    g_pinbuf = NULL;

    /* G6 — typed buffer: elements are REWRITTEN to the promoted address. */
    setup_g6();
    scrub();
    CLEAR_CALLEE_SAVED();
    pins0 = _emperor_gc_debug_pin_count();
    _emperor_gc_collect();
    if (g_tbuf[0] == g_old_s) {
        if (_emperor_gc_debug_pin_count() == pins0) {
            fprintf(stderr, "FAIL G6: typed element neither rewritten nor pinned\n");
            return 1;
        }
    }
    if (g_tbuf[0] == NULL || ((char*)g_tbuf[0])[0] != 'T') {
        fprintf(stderr, "FAIL G6: typed-buffer element corrupted\n");
        return 1;
    }
    _emperor_gc_untrack_buffer(g_tbuf);
    free(g_tbuf);
    g_tbuf = NULL;

    /* G7 — large object goes straight to the old generation: never moves. */
    g_big = _emperor_gc_alloc(20000, 0);
    void* old_big = g_big;
    _emperor_gc_add_root(&g_big);
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    if (g_big != old_big) {
        fprintf(stderr, "FAIL G7: large object moved\n");
        return 1;
    }
    _emperor_gc_remove_root(&g_big);
    g_big = NULL;

    /* G9 — card granularity: same old object, one barrier'd (twice-stored)
     * slot and one deliberately UNbarrier'd slot — the shared dirty card
     * must rescue both, judging current values. */
    setup_g9();
    scrub();
    CLEAR_CALLEE_SAVED();
    pins0 = _emperor_gc_debug_pin_count();
    _emperor_gc_collect();
    if (g_hold->in.name == g_old_in || g_hold->s == g_old_s) {
        if (_emperor_gc_debug_pin_count() == pins0) {
            fprintf(stderr, "FAIL G9: card-scanned slots neither promoted nor pinned "
                    "[in.name=%p/%p s=%p/%p]\n",
                    g_hold->in.name, g_old_in, g_hold->s, g_old_s);
            return 1;
        }
    }
    if (g_hold->in.name == NULL || ((char*)g_hold->in.name)[0] != 'N' ||
        g_hold->s == NULL || ((char*)g_hold->s)[0] != 'W') {
        fprintf(stderr, "FAIL G9: card-scanned children corrupted "
                "(in.name=%p s=%p)\n", g_hold->in.name, g_hold->s);
        return 1;
    }

    /* G10 — NULL overwrite after the card was marked: slot stays NULL. */
    setup_g10();
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();
    if (g_hold->s != NULL) {
        fprintf(stderr, "FAIL G10: NULL overwrite not honored by the card scan "
                "(s=%p)\n", g_hold->s);
        return 1;
    }
    if (g_hold->in.name == NULL || ((char*)g_hold->in.name)[0] != 'N') {
        fprintf(stderr, "FAIL G10: unrelated slot disturbed by the card scan\n");
        return 1;
    }

    /* Drop everything: after a full collect the nursery section's bytes are
     * gone (managed drops back near the pre-section baseline). */
    g_hold = NULL;
    scrub();
    CLEAR_CALLEE_SAVED();
    _emperor_gc_collect();

    printf("generational section: promotion/forwarding/barriers/finalizers/pin/typed all pass\n");
    return 0;
}

int main(void) {
    int stack_anchor = 0;
    _emperor_gc_init(&stack_anchor);
    _emperor_gc_add_root((void**)&kept_head);
    _emperor_gc_add_root((void**)&g_hold);
    for (int gi = 0; gi < G8_KEPT; gi++) _emperor_gc_add_root((void**)&g8_kept[gi]);

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
    if (generational_section()) return 1;
    if (auto_minor_section()) return 1;

    printf("PASS: survivors intact, garbage reclaimed, index stable, ref-maps precise\n");
    return 0;
}
