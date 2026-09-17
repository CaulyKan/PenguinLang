#ifndef EMPEROR_GC_H
#define EMPEROR_GC_H

#include <stdint.h>
#include <stddef.h>

/* Initialize the GC. Must be called once at program start. */
void _emperor_gc_init(void* stack_bottom);

/* Register/unregister a global pointer as a GC root. */
void _emperor_gc_add_root(void** root);
void _emperor_gc_remove_root(void** root);

/* Safepoint poll (GC v2): the emitter calls this before every call and at
 * every allocation site; it collects when _emperor_gc_alloc raised the
 * pending flag. Cheap no-op otherwise. */
void _emperor_gc_poll(void);

/* The current stack's precise frame-chain head (emitted code links a
 * per-function EmperorGcFrame onto it; the scheduler swaps it per
 * coroutine — see gc.c). */
extern void* _emperor_gc_frame_head;
extern int _emperor_gc_want_collect;

/* Register/unregister a RAW (non-GC) buffer whose CONTENTS include pointers
 * to GC objects (std container element storage). The collector scans every
 * registered region like an extension of the stack. Containers must remove
 * the region when they free or replace the buffer (dispose_mem / _grow). */
void _emperor_gc_scan_add(void* base, size_t bytes);
void _emperor_gc_scan_remove(void* base);

/* Typed buffer registration (GC v2 phase 3b): like _gc_scan_add but the
 * buffer holds COUNT elements of STRIDE bytes whose reference layout is
 * described by ELEM_MAP (an emperor ref-map program for ONE element; NULL =
 * each element is a single bare reference). Precise: a minor collection
 * rewrites young references in place. Remove with _gc_untrack_buffer.
 * _emperor_gc_bare_refmap is the shared map for bare-ref element buffers. */
void _emperor_gc_track_buffer(void* base, uint64_t count, uint64_t stride,
                              const int32_t* elem_map);
void _emperor_gc_untrack_buffer(void* base);
extern const int32_t _emperor_gc_bare_refmap[5];

/* Write barriers (GC v3): NO-OPS. The collector is single-generation and
 * stop-the-world on the only mutator, so no remembered set exists. The
 * symbols remain for ABI compatibility — emissions still carry the calls
 * until the emitter change retires them (and previously emitted .ll and
 * .penguin-lib consumers keep linking unchanged). */
void _emperor_gc_write_barrier(void* obj, void** slot);
void _emperor_gc_write_barrier_map(void* obj, void* slot, const int32_t* map);

/* Debug introspection (test-only): split of _emperor_gc_info() into the
 * old-generation malloc-heap bytes and the live nursery bytes, plus the
 * cumulative conservative-pin count (objects kept at their address by a
 * conservative word — tests accept pin OR promotion as a valid outcome). */
void _emperor_gc_info_split(uint64_t* old_bytes, uint64_t* young_bytes);
uint64_t _emperor_gc_debug_pin_count(void);

/* Debug introspection (test-only): the heap charge of a hypothetical
 * tracked allocation of SIZE body bytes — the size-class slot under
 * EMPEROR_GC_MODE=greentea, else header + round8(size). gc_torture's
 * exact live/die delta assertions use this so they hold in every mode. */
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
