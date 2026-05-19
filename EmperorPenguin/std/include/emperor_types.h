#ifndef EMPEROR_TYPES_H
#define EMPEROR_TYPES_H

/*
 * EmperorPenguin C Runtime Types
 * These structs match the LLVM IR data layout emitted by the compiler.
 */

typedef struct EmperorInterfaceMapEntry {
    const char* interface_id;
    void** method_table;
} EmperorInterfaceMapEntry;

typedef struct EmperorClassMetadata {
    const char* name;
    int instance_size;
    int field_count;
    int* field_offsets;
    int* field_is_ptr;
    void** virtual_method_table;
    int interface_count;
    EmperorInterfaceMapEntry* interface_map;
    void (*destructor)(void*);
} EmperorClassMetadata;

#endif
