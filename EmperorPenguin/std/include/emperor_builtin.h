#ifndef EMPEROR_BUILTIN_H
#define EMPEROR_BUILTIN_H

/* Print a string followed by a newline. */
void _emperor_println(const char* s);

/* Print a string without a newline. */
void _emperor_print(const char* s);

/* Allocate a GC-tracked object of the given size. */
void* _emperor_alloc_impl(int size);

/* Convert i32 to a GC-tracked string. */
char* _emperor_int_to_string(int value);

/* Convert i64 to a GC-tracked string. */
char* _emperor_i64_to_string(long long value);

/* Concatenate two strings into a GC-tracked result. */
char* _emperor_string_concat(const char* a, const char* b);

/* Convert bool to a GC-tracked string ("true"/"false"). */
char* _emperor_bool_to_string(char value);

#endif
