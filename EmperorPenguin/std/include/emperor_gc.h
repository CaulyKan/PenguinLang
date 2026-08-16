#ifndef EMPEROR_GC_H
#define EMPEROR_GC_H

#include <stdint.h>
#include <stddef.h>

/* Initialize the GC. Must be called once at program start. */
void _emperor_gc_init(void* stack_bottom);

/* Register a global pointer as a GC root. */
void _emperor_gc_add_root(void** root);

/* Register/unregister a RAW (non-GC) buffer whose CONTENTS include pointers
 * to GC objects (std container element storage). The collector scans every
 * registered region like an extension of the stack. Containers must remove
 * the region when they free or replace the buffer (dispose_mem / _grow). */
void _emperor_gc_scan_add(void* base, size_t bytes);
void _emperor_gc_scan_remove(void* base);

/* Trigger an immediate garbage collection. */
void _emperor_gc_collect(void);

/* Return the total bytes currently managed by the GC. */
uint64_t _emperor_gc_info(void);

/* GC-tracked allocation (internal, used by other runtime functions). */
void* _emperor_gc_alloc(int size, int is_string);

#endif
