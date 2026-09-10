#ifndef EMPEROR_TYPES_H
#define EMPEROR_TYPES_H

#include <stdint.h>

/*
 * EmperorPenguin C Runtime Types
 * These structs match the LLVM IR data layout emitted by the compiler.
 */

typedef struct EmperorInterfaceMapEntry {
    const char* interface_id;
    void** method_table;
} EmperorInterfaceMapEntry;

/* Precise-GC ref-map: a flat int32 program describing every location that can
 * hold a pointer to a GC object inside one value of the described type. The
 * LLVMEmitter computes one per class/enum layout (see LLVMEmitter.penguin,
 * refmap_append_* — the emitter is the encoding's single writer, this struct's
 * collectors the single reader; they evolve in lockstep, gated by bootstrap
 * convergence).
 *
 *   map[0] = total element count N (map[1..N) is the program)
 *   root node starts at index 1; sub-node indices are absolute element
 *   indices, so 0 unambiguously means "nothing to scan".
 *
 * Node forms (kind, n, payload...):
 *   SLOTS (1): 1, n, (byte_off, sub) * n
 *       sub = -1: a bare pointer slot at byte_off (reference/string field);
 *       sub >= 2: an INLINE nested struct (value class / enum) at byte_off,
 *                 described by node #sub with offsets relative to byte_off.
 *       n = 0: the type holds no pointers at all.
 *   ENUM  (2): 2, n, sub * n
 *       sub[tag] selects the node describing the payload of variant `tag`
 *       (stored as the i64 at base+8); the payload is INLINE at base+16.
 *       sub = 0: this variant's payload holds no pointers.
 *   REFARR (3): 3, elem_stride, ptr_off
 *       a repeated element layout (container buffer regions); the live
 *       element count comes from the region registration, not the map.
 *
 * refmap == NULL on an object means "no program available" (foreign/C-side
 * metadata) — the collector falls back to conservative body scanning for that
 * object, which is never unsound. */
typedef struct EmperorClassMetadata {
    const char* name;
    int instance_size; /* NOTE: _emperor_ICopy_copy reads name@0 and
                          instance_size@8 through RAW OFFSETS — the leading
                          fields must not move. */
    int field_count;
    int* field_offsets;
    int* field_is_ptr;
    void** virtual_method_table;
    int interface_count;
    EmperorInterfaceMapEntry* interface_map;
    void (*destructor)(void*);
    const int32_t* refmap;
} EmperorClassMetadata;

#endif
