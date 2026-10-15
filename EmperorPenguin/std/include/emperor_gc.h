#ifndef EMPEROR_GC_H
#define EMPEROR_GC_H

#include <stdint.h>
#include <stddef.h>

/* Initialize the GC. Must be called once at program start. */
void _emperor_gc_init(void* stack_bottom);

/* Register/unregister a global pointer as a GC root. */
void _emperor_gc_add_root(void** root);
void _emperor_gc_remove_root(void** root);

/* Safepoint poll: the emitter calls this before every call and at
 * every allocation site; it collects when _emperor_gc_alloc raised the
 * pending flag. Cheap no-op otherwise. */
void _emperor_gc_poll(void);

/* The CURRENT STACK's precise frame-chain head (emitted code links a
 * per-function EmperorGcFrame onto it; the scheduler swaps it per
 * coroutine — see gc.c). THREAD-LOCAL: the mt kernel switches coroutines
 * in parallel on N workers and each thread's main stack carries its OWN
 * chain — a plain global made the per-coroutine swap in _co_switch_in
 * clobber heads across threads (mixed chains → CORRUPT REF-MAP aborts,
 * reliably at EMPEROR_THREADS=8). Emitted code goes through the two
 * accessors below (a thread_local global cannot be referenced from the
 * IR: JIT modules would resolve it to a non-aliasing emutls clone). */
extern _Thread_local void* _emperor_gc_frame_head;
extern int _emperor_gc_want_collect;
void* _emperor_gc_frame_get(void);
void _emperor_gc_frame_set(void* head);

/* Register/unregister a RAW (non-GC) buffer whose CONTENTS include pointers
 * to GC objects (std container element storage). The collector scans every
 * registered region like an extension of the stack. Containers must remove
 * the region when they free or replace the buffer (dispose_mem / _grow). */
void _emperor_gc_scan_add(void* base, size_t bytes);
void _emperor_gc_scan_remove(void* base);

/* Typed buffer registration: like _gc_scan_add but the buffer holds
 * COUNT elements of STRIDE bytes whose reference layout is described by
 * ELEM_MAP (an emperor ref-map program for ONE element; NULL = each
 * element is a single bare reference). The walk is precise: only declared
 * pointer slots are read. Remove with _gc_untrack_buffer.
 * _emperor_gc_bare_refmap is the shared map for bare-ref element buffers. */
void _emperor_gc_track_buffer(void* base, uint64_t count, uint64_t stride,
                              const int32_t* elem_map);
void _emperor_gc_untrack_buffer(void* base);
extern const int32_t _emperor_gc_bare_refmap[5];


/* Debug introspection (test-only): split of _emperor_gc_info() into the
 * malloc large-object heap's bytes and the span heap's exact live bytes
 * (unflushed arena deltas included). */
void _emperor_gc_info_split(uint64_t* old_bytes, uint64_t* young_bytes);

/* Debug introspection (test-only): the heap charge of a hypothetical
 * tracked allocation of SIZE body bytes — the size-class slot, or
 * header + round8(size) on the malloc large path. gc_torture's exact
 * live/die delta assertions use this. */
size_t _emperor_gc_alloc_charge(int size);

/* Runtime ABI tag — consumers mixing emissions against foreign runtimes
 * compare this and refuse (see gc.c). */
extern const char* const _emperor_runtime_abi;

/* Trigger an immediate garbage collection. */
void _emperor_gc_collect(void);

/* Return the total bytes currently managed by the GC. */
uint64_t _emperor_gc_info(void);

/* GC-tracked allocation (internal, used by other runtime functions). */
void* _emperor_gc_alloc(int size, int is_string);

#endif
