#ifndef EMPEROR_INTEROP_H
#define EMPEROR_INTEROP_H

#include "emperor_types.h"

/* Look up a virtual method via interface map dispatch. */
void* _emperor_vtable_lookup(void* obj, const char* interface_id, int slot);

/* Check if an object implements a given interface. Returns 1 or 0. */
int _emperor_isinstance(void* obj, const char* interface_id);

/* Check if an object is an instance of a specific class. Returns 1 or 0. */
int _emperor_check_class(void* obj, const char* class_id);

#endif
