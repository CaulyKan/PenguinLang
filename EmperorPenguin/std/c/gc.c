#include "emperor_types.h"
#include "emperor_gc.h"
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <setjmp.h>

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
/* DIAGNOSTIC: set to 1 to effectively disable GC (8TB threshold) so we can tell
 * whether a crash is GC-induced (goes away when disabled) or a pure logic bug. */
#define EMPEROR_GC_DISABLED 1

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

#define EMPEROR_GC_MAX_ROOTS 4096
static void** _emperor_gc_global_roots[EMPEROR_GC_MAX_ROOTS];
static int _emperor_gc_global_root_count = 0;

void _emperor_gc_add_root(void** root) {
    if (_emperor_gc_global_root_count < EMPEROR_GC_MAX_ROOTS) {
        _emperor_gc_global_roots[_emperor_gc_global_root_count++] = root;
    }
}

/* ---- GC Mark ---- */

static void _emperor_gc_mark_object(void* obj);

static int _emperor_gc_is_tracked(void* candidate) {
    GCHeader* h = _emperor_gc_allocation_list;
    while (h) {
        char* user_ptr = (char*)h + sizeof(GCHeader);
        if (candidate == user_ptr) return 1;
        h = h->next;
    }
    return 0;
}

static void _emperor_gc_mark_conservative(void* stack_bottom, void* stack_top) {
    void** ptr = (void**)stack_top;
    while (ptr < (void**)stack_bottom) {
        void* candidate = *ptr;
        if (_emperor_gc_is_tracked(candidate)) {
            _emperor_gc_mark_object(candidate);
        }
        ptr++;
    }
}

static void _emperor_gc_mark_object(void* obj) {
    if (!obj) return;
    GCHeader* header = (GCHeader*)((char*)obj - sizeof(GCHeader));
    if (header->marked) return;
    header->marked = 1;

    if (header->is_string) return;

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
    void** ptr = (void**)obj;
    size_t word_count = (size_t)header->size / sizeof(void*);
    size_t i;
    for (i = 0; i < word_count; i++) {
        void* candidate = ptr[i];
        if (_emperor_gc_is_tracked(candidate)) {
            _emperor_gc_mark_object(candidate);
        }
    }
}

/* ---- GC Sweep ---- */

static size_t _emperor_gc_sweep(void) {
    size_t freed = 0;
    GCHeader** prev = &_emperor_gc_allocation_list;
    GCHeader* curr = _emperor_gc_allocation_list;
    while (curr) {
        if (curr->marked) {
            curr->marked = 0;
            prev = &curr->next;
            curr = curr->next;
        } else {
            GCHeader* dead = curr;
            *prev = curr->next;
            curr = curr->next;
            freed += sizeof(GCHeader) + dead->size;
            free(dead);
        }
    }
    return freed;
}

/* ---- GC Collect ---- */

void _emperor_gc_collect(void) {
    if (!_emperor_gc_stack_bottom) return;

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

    int i;
    for (i = 0; i < _emperor_gc_global_root_count; i++) {
        void* obj = *(void**)_emperor_gc_global_roots[i];
        _emperor_gc_mark_object(obj);
    }

    _emperor_gc_mark_conservative(_emperor_gc_stack_bottom, stack_top);

    size_t freed = _emperor_gc_sweep();
    _emperor_gc_total_allocated -= freed;

    if (freed < _emperor_gc_total_allocated / 4) {
        _emperor_gc_threshold *= 2;
    }
}

/* ---- GC Init ---- */

void _emperor_gc_init(void) {
    _emperor_gc_allocation_list = NULL;
    _emperor_gc_total_allocated = 0;
    _emperor_gc_threshold = EMPEROR_GC_DISABLED ? (8ULL << 40) : (256 * 1024);
    _emperor_gc_global_root_count = 0;
    _emperor_gc_stack_bottom = _emperor_gc_get_stack_pointer();
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

    if (_emperor_gc_total_allocated >= _emperor_gc_threshold) {
        _emperor_gc_collect();
    }

    return (char*)header + sizeof(GCHeader);
}
