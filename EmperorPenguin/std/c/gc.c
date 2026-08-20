#include "emperor_types.h"
#include "emperor_gc.h"
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdio.h>
#include <setjmp.h>

/* Conservative stack/region scanning reads every word in range, including
 * sanitizer stack redzones — exempt it from ASan so instrumented builds of
 * the runtime don't abort on a benign read. */
#if defined(__has_feature)
#  if __has_feature(address_sanitizer)
#    define EMPEROR_NO_ASAN __attribute__((no_sanitize("address")))
#  endif
#endif
#ifndef EMPEROR_NO_ASAN
#  define EMPEROR_NO_ASAN
#endif

/* ---- GC Header ---- */

typedef struct GCHeader {
    struct GCHeader* next;
    int marked;
    int is_string;
    int size;
} GCHeader;

static GCHeader* _emperor_gc_allocation_list = NULL;
static size_t _emperor_gc_total_allocated = 0;
static size_t _emperor_gc_threshold = 256 * 1024; /* 256KB initial */

/* Runtime kill-switch: set EMPEROR_GC_DISABLE=1 in the environment to raise
 * the threshold to 8TB so automatic collection never fires (diagnostics —
 * tells GC-induced crashes apart from logic bugs without recompiling).
 * Explicit gc_collect() calls are never gated. */
static int _emperor_gc_disabled = 0;

/* ---- Tracked-pointer hash set ----
 * Open-addressing set of user pointers backing _emperor_gc_is_tracked so the
 * mark phase costs O(1) per candidate word instead of walking the whole
 * allocation list (which made marking O(candidates x objects)). Exact-pointer
 * matching only — identical semantics to the old list walk. The set is never
 * deleted from incrementally: sweep clears and rebuilds it from the surviving
 * allocation list in one pass. */

typedef struct {
    void** slots;   /* NULL = empty */
    size_t capacity; /* power of two, 0 until first insert */
    size_t count;
} GCPtrSet;

static GCPtrSet _emperor_gc_tracked = { NULL, 0, 0 };

static size_t gc_ptr_set_index(void* p, size_t mask) {
    /* Fibonacci hashing: malloc returns >=8-aligned pointers, so drop the low
     * 4 bits and multiply by the golden-ratio constant to spread sequential
     * allocations across the whole table. */
    uintptr_t h = ((uintptr_t)p >> 4) * 0x9E3779B97F4A7C15ULL;
    return (size_t)h & mask;
}

static void gc_ptr_set_clear(GCPtrSet* set) {
    set->count = 0;
    if (set->slots) {
        memset(set->slots, 0, set->capacity * sizeof(void*));
    }
}

static int gc_ptr_set_grow(GCPtrSet* set) {
    size_t new_capacity = set->capacity ? set->capacity * 2 : 1024;
    void** new_slots = (void**)malloc(new_capacity * sizeof(void*));
    if (!new_slots) return 0;
    memset(new_slots, 0, new_capacity * sizeof(void*));
    size_t new_mask = new_capacity - 1;
    for (size_t i = 0; i < set->capacity; i++) {
        void* p = set->slots[i];
        if (!p) continue;
        size_t j = gc_ptr_set_index(p, new_mask);
        while (new_slots[j]) j = (j + 1) & new_mask;
        new_slots[j] = p;
    }
    free(set->slots);
    set->slots = new_slots;
    set->capacity = new_capacity;
    return 1;
}

static void gc_ptr_set_insert(GCPtrSet* set, void* p) {
    if ((set->count + 1) * 10 > set->capacity * 7) {
        /* Grow eagerly; if the rehash malloc fails keep the old table — the
         * load factor just exceeds 0.7 until a later grow succeeds. */
        gc_ptr_set_grow(set);
    }
    if (set->capacity == 0) return; /* first grow failed and nothing to probe */
    size_t mask = set->capacity - 1;
    size_t i = gc_ptr_set_index(p, mask);
    while (set->slots[i]) {
        if (set->slots[i] == p) return; /* already present */
        i = (i + 1) & mask;
    }
    set->slots[i] = p;
    set->count++;
}

/* gc_ptr_set_contains was removed when marking switched to interior-aware
 * block resolution (gc_resolve_block); the set itself is still rebuilt during
 * sweep as the exact-match index for fast-path lookups. */

/* ---- Interior-pointer resolution ----
 * Inline value-type layouts (value-class fields nested inside heap objects,
 * enum payloads, ref<ValueClass> bindings) produce pointers INTO the middle
 * of a GC allocation, not to its base. Exact-pointer matching then misses the
 * owner, the object is swept while still referenced through that interior
 * pointer, and the program corrupts the heap (tcache reuse overwrites the
 * still-used object). The sorted-starts index resolves any candidate address
 * to its owning block (greatest start <= candidate, candidate < start+size)
 * with one binary search, so marking treats interior pointers as roots of
 * their owner — conservative (may retain a dead block a stray integer points
 * into) but never frees a live one. Rebuilt once per collection; allocation
 * between rebuilds is fine because a fresh block cannot be pointed to by a
 * stale interior pointer (it did not exist when the pointer was created). */
static void** _emperor_gc_sorted = NULL;
static size_t _emperor_gc_sorted_count = 0;
static size_t _emperor_gc_sorted_capacity = 0;

static int gc_ptr_cmp(const void* a, const void* b) {
    void* pa = *(void* const*)a;
    void* pb = *(void* const*)b;
    return pa < pb ? -1 : (pa > pb ? 1 : 0);
}

static int gc_rebuild_sorted(void) {
    size_t n = 0;
    for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) n++;
    if (n > _emperor_gc_sorted_capacity) {
        size_t new_capacity = n * 2;
        void** grown = (void**)realloc(_emperor_gc_sorted, new_capacity * sizeof(void*));
        if (!grown) { _emperor_gc_sorted_count = 0; return 0; }
        _emperor_gc_sorted = grown;
        _emperor_gc_sorted_capacity = new_capacity;
    }
    size_t i = 0;
    for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next)
        _emperor_gc_sorted[i++] = (char*)h + sizeof(GCHeader);
    qsort(_emperor_gc_sorted, n, sizeof(void*), gc_ptr_cmp);
    _emperor_gc_sorted_count = n;
    return 1;
}

/* Resolve a candidate address to its owning block's user pointer, or NULL.
 * Exact bases resolve too (candidate == start). */
static void* gc_resolve_block(void* candidate) {
    if (_emperor_gc_sorted_count == 0) return NULL;
    char* c = (char*)candidate;
    size_t lo = 0, hi = _emperor_gc_sorted_count;
    /* find greatest index with sorted[idx] <= candidate */
    while (lo + 1 < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if ((char*)_emperor_gc_sorted[mid] <= c) lo = mid;
        else hi = mid;
    }
    if ((char*)_emperor_gc_sorted[lo] > c) return NULL;
    GCHeader* h = (GCHeader*)((char*)_emperor_gc_sorted[lo] - sizeof(GCHeader));
    if (c < (char*)_emperor_gc_sorted[lo] + h->size) return _emperor_gc_sorted[lo];
    return NULL;
}

/* ---- Platform-specific stack pointer ---- */

#if defined(__x86_64__)
static inline void* _emperor_gc_get_stack_pointer(void) {
    void* sp;
    __asm__ volatile ("mov %%rsp, %0" : "=r"(sp));
    return sp;
}
#elif defined(__aarch64__)
static inline void* _emperor_gc_get_stack_pointer(void) {
    void* sp;
    __asm__ volatile ("mov %0, sp" : "=r"(sp));
    return sp;
}
#else
static inline void* _emperor_gc_get_stack_pointer(void) {
    return __builtin_frame_address(0);
}
#endif

static void* _emperor_gc_stack_bottom = NULL;

/* ---- GC Roots ---- */

#define EMPEROR_GC_MAX_ROOTS 65536
static void** _emperor_gc_global_roots[EMPEROR_GC_MAX_ROOTS];
static int _emperor_gc_global_root_count = 0;

void _emperor_gc_add_root(void** root) {
    if (_emperor_gc_global_root_count < EMPEROR_GC_MAX_ROOTS) {
        _emperor_gc_global_roots[_emperor_gc_global_root_count++] = root;
    } else {
        /* Dropping a root silently would let the collector free a live
         * global; make the overflow loud instead. */
        static int warned = 0;
        if (!warned) {
            fprintf(stderr, "emperor gc: root registry overflow (%d), further globals are NOT rooted\n", EMPEROR_GC_MAX_ROOTS);
            warned = 1;
        }
    }
}

/* ---- External scan regions ----
 * Raw (non-GC) buffers that CONTAIN pointers to GC objects: std container
 * element storage (Vector<T>/HashMap<K,V> buffers, Array<T,N> with reference
 * elements). The conservative stack/object scan cannot see inside malloc'd
 * memory, so containers register their buffers here and marking treats each
 * region as an extension of the stack. Containers unregister in dispose_mem
 * — which the GC finalizer runs on dead instances — so the finalizer is
 * load-bearing for correctness: a region that outlives its buffer only
 * retains garbage (safe), while a buffer missing its region would have live
 * elements swept (unsafe). */

typedef struct {
    char* base;
    size_t bytes;
} GCScanRegion;

static GCScanRegion* _emperor_gc_scan_regions = NULL;
static size_t _emperor_gc_scan_region_count = 0;
static size_t _emperor_gc_scan_region_capacity = 0;

void _emperor_gc_scan_add(void* base, size_t bytes) {
    if (!base || bytes == 0) return;
    if (_emperor_gc_scan_region_count == _emperor_gc_scan_region_capacity) {
        size_t new_capacity = _emperor_gc_scan_region_capacity ? _emperor_gc_scan_region_capacity * 2 : 64;
        GCScanRegion* grown = (GCScanRegion*)realloc(_emperor_gc_scan_regions, new_capacity * sizeof(GCScanRegion));
        if (!grown) {
            /* Losing this registration would let the collector sweep every
             * GC object stored in the buffer; there is no safe fallback. */
            fprintf(stderr, "emperor gc: scan-region registry exhausted, aborting\n");
            abort();
        }
        _emperor_gc_scan_regions = grown;
        _emperor_gc_scan_region_capacity = new_capacity;
    }
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].base = (char*)base;
    _emperor_gc_scan_regions[_emperor_gc_scan_region_count].bytes = bytes;
    _emperor_gc_scan_region_count++;
}

void _emperor_gc_scan_remove(void* base) {
    for (size_t i = 0; i < _emperor_gc_scan_region_count; i++) {
        if (_emperor_gc_scan_regions[i].base == (char*)base) {
            _emperor_gc_scan_regions[i] = _emperor_gc_scan_regions[_emperor_gc_scan_region_count - 1];
            _emperor_gc_scan_region_count--;
            return;
        }
    }
}

/* ---- GC Mark ----
 * Iterative mark with an explicit worklist: the old recursive marker could
 * blow the C stack on long linked structures (compiler IR lists). An object
 * is marked when pushed, so each node enters the worklist at most once. */

static GCHeader** _emperor_gc_mark_stack = NULL;
static size_t _emperor_gc_mark_stack_capacity = 0;
static size_t _emperor_gc_mark_stack_top = 0;

/* Set when a worklist push fails mid-marking. Marking is then partial, so the
 * collection must NOT sweep (an unmarked live object would be freed); the
 * collector retains everything that cycle instead. */
static int _emperor_gc_mark_failed = 0;

static int gc_mark_stack_push(GCHeader* h) {
    if (_emperor_gc_mark_stack_top == _emperor_gc_mark_stack_capacity) {
        size_t new_capacity = _emperor_gc_mark_stack_capacity ? _emperor_gc_mark_stack_capacity * 2 : 256;
        GCHeader** grown = (GCHeader**)realloc(_emperor_gc_mark_stack, new_capacity * sizeof(GCHeader*));
        if (!grown) return 0;
        _emperor_gc_mark_stack = grown;
        _emperor_gc_mark_stack_capacity = new_capacity;
    }
    _emperor_gc_mark_stack[_emperor_gc_mark_stack_top++] = h;
    return 1;
}

/* obj must be a validated tracked pointer (or NULL). */
static EMPEROR_NO_ASAN void _emperor_gc_mark_object(void* obj) {
    if (!obj || _emperor_gc_mark_failed) return;
    GCHeader* header = (GCHeader*)((char*)obj - sizeof(GCHeader));
    if (header->marked) return;
    header->marked = 1;
    if (!gc_mark_stack_push(header)) { _emperor_gc_mark_failed = 1; return; }

    while (_emperor_gc_mark_stack_top > 0) {
        GCHeader* cur = _emperor_gc_mark_stack[--_emperor_gc_mark_stack_top];
        if (cur->is_string) continue;

        /* Conservative scan of the whole object body. The class metadata at
         * offset 0 is a global constant (never GC-tracked, so the is_tracked
         * check naturally skips it). This is necessary because a field can be
         * a struct that *contains* pointers without the field itself being a
         * single pointer — e.g. `Option<T>` lays out as { ptr metadata, i32 tag,
         * ptr payload }, and `ref<T>`/node-link fields are reached through those
         * inner payload pointers. The precise `field_is_ptr` metadata only flags
         * fields that are a bare pointer, so relying on it alone lets the GC miss
         * pointers nested inside Option/struct fields and prematurely free live
         * objects (use-after-free -> heap corruption). Scanning every word and
         * marking any that resolve to a tracked allocation is conservative but
         * sound: a stray integer that looks like a pointer at worst keeps a dead
         * object alive, never frees a live one. */
        void** ptr = (void**)((char*)cur + sizeof(GCHeader));
        size_t word_count = (size_t)cur->size / sizeof(void*);
        for (size_t i = 0; i < word_count; i++) {
            void* candidate = ptr[i];
            void* owner = gc_resolve_block(candidate);
            if (owner) {
                GCHeader* child = (GCHeader*)((char*)owner - sizeof(GCHeader));
                if (!child->marked) {
                    child->marked = 1;
                    if (!gc_mark_stack_push(child)) { _emperor_gc_mark_failed = 1; return; }
                }
            }
        }
    }
}

static EMPEROR_NO_ASAN void _emperor_gc_mark_conservative(void* stack_bottom, void* stack_top) {
    void** ptr = (void**)stack_top;
    while (ptr < (void**)stack_bottom) {
        void* candidate = *ptr;
        void* owner = gc_resolve_block(candidate);
        if (owner) {
            _emperor_gc_mark_object(owner);
        }
        ptr++;
    }
}

/* ---- GC Sweep ---- */

/* Run the class finalizer of a dead object, if any. Non-string objects carry
 * an EmperorClassMetadata* at offset 0 (stamped by codegen right after
 * allocation; a freshly allocated, not-yet-stamped object is still zeroed and
 * skipped via the NULL check). Classes implementing IMemoryDispose have
 * dispose_mem in the destructor slot — void(void*), exactly the slot's
 * signature — releasing raw malloc'd buffers (Array<T,N>.buf, ...) that the
 * collector cannot see. */
static void _emperor_gc_finalize(GCHeader* h) {
    if (h->is_string) return;
    if (h->size < (int)sizeof(void*)) return;
    void* user = (char*)h + sizeof(GCHeader);
    EmperorClassMetadata* meta = *(EmperorClassMetadata**)user;
    if (meta && meta->destructor) {
        meta->destructor(user);
    }
}

static size_t _emperor_gc_sweep(void) {
    size_t freed = 0;
    /* Phase A: unlink all dead objects first (no user code runs here) and
     * rebuild the tracked set from the survivors (a rebuild instead of
     * per-entry deletion: open-addressing deletion needs tombstones or
     * backward shifting, a rebuild is one O(live) pass over the list). */
    gc_ptr_set_clear(&_emperor_gc_tracked);
    GCHeader* pending = NULL;
    GCHeader** prev = &_emperor_gc_allocation_list;
    GCHeader* curr = _emperor_gc_allocation_list;
    while (curr) {
        if (curr->marked) {
            curr->marked = 0;
            gc_ptr_set_insert(&_emperor_gc_tracked, (char*)curr + sizeof(GCHeader));
            prev = &curr->next;
            curr = curr->next;
        } else {
            GCHeader* dead = curr;
            *prev = curr->next;
            curr = curr->next;
            dead->next = pending;
            pending = dead;
        }
    }
    /* Phase B: finalizers run while every dead object is still allocated, so
     * a container finalizer may safely touch the (also dead) objects it owns
     * — e.g. HashMap.dispose_mem reads and disposes its inner Vectors before
     * their own finalizers run. dispose_mem must be idempotent (buf != 0
     * guards in the stdlib containers); a second dispose is a no-op.
     * Allocations made inside a finalizer land on the live list — the
     * collecting flag suppresses nested collection, they get judged next
     * cycle. */
    for (GCHeader* d = pending; d; d = d->next) {
        _emperor_gc_finalize(d);
    }
    /* Phase C: free the dead. */
    while (pending) {
        GCHeader* dead = pending;
        pending = pending->next;
        freed += sizeof(GCHeader) + dead->size;
        free(dead);
    }
    return freed;
}

/* ---- GC Collect ---- */

/* Non-zero while a collection (including its finalizer phase) is running:
 * blocks re-entry from user finalizers calling gc_collect(), and makes
 * finalizer-triggered allocations defer the next automatic collection. */
static int _emperor_gc_collecting = 0;

EMPEROR_NO_ASAN void _emperor_gc_collect(void) {
    if (!_emperor_gc_stack_bottom || _emperor_gc_collecting) return;
    _emperor_gc_collecting = 1;

    /* Spill ALL registers (especially callee-saved ones: rbx, rbp, r12-r15 on
     * x86-64) onto the stack before scanning. A conservative GC that only walks
     * the stack misses any live pointer that is sitting in a register at the
     * moment of collection — those objects then get swept, their memory reused,
     * and the program observes heap corruption (e.g. a SourceLocation.filename
     * pointer that now reads as a numeric pointer value, or a BoundType* whose
     * bits are ASCII from a reused string buffer). setjmp's jmp_buf is a local
     * in this frame, so it lies within [stack_top, stack_bottom) and the scan
     * below covers the spilled register words. This is the standard
     * register-flush technique used by conservative collectors (e.g. Boehm GC).
     * The volatile asm barrier stops the compiler from optimizing the setjmp
     * away or reordering the stack-pointer read above it. */
    jmp_buf _gc_register_buf;
    setjmp(_gc_register_buf);
    __asm__ volatile("" ::: "memory");

    void* stack_top = _emperor_gc_get_stack_pointer();

    /* Resolve interior pointers to their owning block during marking (see
     * gc_resolve_block). If the sorted index cannot be rebuilt (allocation
     * failure), retain everything this cycle instead of risking a partial
     * marking that the exact-match fallback would silently miss. */
    if (!gc_rebuild_sorted()) {
        _emperor_gc_collecting = 0;
        return;
    }

    for (int i = 0; i < _emperor_gc_global_root_count; i++) {
        void* obj = *(void**)_emperor_gc_global_roots[i];
        _emperor_gc_mark_object(obj);
    }

    /* Registered raw buffers (container element storage): scan them like an
     * extension of the stack so GC references inside raw memory stay live. */
    for (size_t r = 0; r < _emperor_gc_scan_region_count; r++) {
        char** p = (char**)_emperor_gc_scan_regions[r].base;
        char** end = (char**)(_emperor_gc_scan_regions[r].base + _emperor_gc_scan_regions[r].bytes);
        for (; p < end; p++) {
            void* candidate = *p;
            void* owner = gc_resolve_block(candidate);
            if (owner) {
                _emperor_gc_mark_object(owner);
            }
        }
    }

    _emperor_gc_mark_conservative(_emperor_gc_stack_bottom, stack_top);

    if (_emperor_gc_mark_failed) {
        /* Partial marking happened — nothing may be swept this cycle. Reset
         * flags (and the worklist top; stale entries are already-marked
         * headers that a later pass would skip anyway) and retain all. */
        _emperor_gc_mark_failed = 0;
        _emperor_gc_mark_stack_top = 0;
        for (GCHeader* h = _emperor_gc_allocation_list; h; h = h->next) h->marked = 0;
        _emperor_gc_collecting = 0;
        return;
    }

    size_t freed = _emperor_gc_sweep();
    _emperor_gc_total_allocated -= freed;

    if (freed < _emperor_gc_total_allocated / 4) {
        _emperor_gc_threshold *= 2;
    }
    _emperor_gc_collecting = 0;
}

/* ---- GC Init ---- */

void _emperor_gc_init(void* stack_bottom) {
    _emperor_gc_allocation_list = NULL;
    _emperor_gc_total_allocated = 0;
    _emperor_gc_threshold = 256 * 1024;
    _emperor_gc_global_root_count = 0;
    _emperor_gc_scan_region_count = 0;
    _emperor_gc_stack_bottom = stack_bottom;
    const char* kill = getenv("EMPEROR_GC_DISABLE");
    _emperor_gc_disabled = (kill != NULL && kill[0] != '\0' && kill[0] != '0');
    if (_emperor_gc_disabled) {
        _emperor_gc_threshold = 8ULL << 40;
    }
    gc_ptr_set_clear(&_emperor_gc_tracked);
}

/* ---- GC Info ---- */

uint64_t _emperor_gc_info(void) {
    return (uint64_t)_emperor_gc_total_allocated;
}

/* ---- GC-tracked Allocation ---- */

void* _emperor_gc_alloc(int size, int is_string) {
    int total = (int)sizeof(GCHeader) + size;
    GCHeader* header = (GCHeader*)malloc(total);
    if (!header) return NULL;
    memset(header, 0, total);
    header->next = _emperor_gc_allocation_list;
    header->marked = 0;
    header->is_string = is_string;
    header->size = size;
    _emperor_gc_allocation_list = header;
    _emperor_gc_total_allocated += total;
    gc_ptr_set_insert(&_emperor_gc_tracked, (char*)header + sizeof(GCHeader));

    /* The threshold collection below may run BEFORE this pointer is returned
     * to the caller — at that moment nothing on the stack may hold the USER
     * pointer yet (callers keep locals around the header base or nothing at
     * all), so an unmarked fresh object would be swept by the very collection
     * its allocation triggered and the caller would receive a dangling
     * pointer (glibc tcache reuse then corrupts the header/list).
     *
     * Protect it ONLY across a collection that actually runs: mark_object
     * treats `marked` as already-processed and SKIPS THE BODY SCAN, so a
     * leaked marked=1 (collection skipped or aborted) would silently unroot
     * everything the object references at the next cycle. Setting the flag
     * inside the branch and clearing it after the call bounds the protection
     * to this cycle — clearing is a no-op when the sweep ran (it resets
     * survivors to 0 itself) and repairs the flag when the collection bailed
     * early (e.g. the mark-failure path). Skipping the fresh object's body
     * scan is safe: the body is still zero-filled at this point. */
    if (!_emperor_gc_disabled && !_emperor_gc_collecting &&
        _emperor_gc_total_allocated >= _emperor_gc_threshold) {
        header->marked = 1;
        _emperor_gc_collect();
        header->marked = 0;
    }

    return (char*)header + sizeof(GCHeader);
}
