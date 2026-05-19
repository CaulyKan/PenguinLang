#include "emperor_interop.h"
#include <string.h>

void* _emperor_vtable_lookup(void* obj, const char* interface_id, int slot) {
    if (!obj) return NULL;

    void** obj_ptr = (void**)obj;
    EmperorClassMetadata* metadata = (EmperorClassMetadata*)(*obj_ptr);
    if (!metadata) return NULL;

    int i;
    for (i = 0; i < metadata->interface_count; i++) {
        EmperorInterfaceMapEntry* entry = &metadata->interface_map[i];
        if (entry->interface_id && strcmp(entry->interface_id, interface_id) == 0) {
            if (entry->method_table) {
                return entry->method_table[slot];
            }
            return NULL;
        }
    }
    return NULL;
}

int _emperor_isinstance(void* obj, const char* interface_id) {
    if (!obj) return 0;

    void** obj_ptr = (void**)obj;
    EmperorClassMetadata* metadata = (EmperorClassMetadata*)(*obj_ptr);
    if (!metadata) return 0;

    int i;
    for (i = 0; i < metadata->interface_count; i++) {
        EmperorInterfaceMapEntry* entry = &metadata->interface_map[i];
        if (entry->interface_id && strcmp(entry->interface_id, interface_id) == 0) {
            return 1;
        }
    }
    return 0;
}

int _emperor_check_class(void* obj, const char* class_id) {
    if (!obj) return 0;

    void** obj_ptr = (void**)obj;
    EmperorClassMetadata* metadata = (EmperorClassMetadata*)(*obj_ptr);
    if (!metadata || !metadata->name) return 0;

    return strcmp(metadata->name, class_id) == 0 ? 1 : 0;
}
