#ifndef EMPEROR_BUILTIN_H
#define EMPEROR_BUILTIN_H

/* Print a string followed by a newline. */
void _emperor_println(const char* s);

/* Print a string without a newline. */
void _emperor_print(const char* s);

/* Print to stderr without a newline. */
void _emperor_eprint(const char* s);

/* Print to stderr followed by a newline. */
void _emperor_eprintln(const char* s);

/* Exit the program with the given code. */
void _emperor_exit(int code);

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

/* Bitwise left shift. */
long long _emperor_lshift(long long value, long long shift);

/* Bitwise right shift. */
long long _emperor_rshift(long long value, long long shift);

/* String helper functions */
long long _emperor_string_length(const char* s);
long long _emperor_string_find(const char* s, const char* sub);
long long _emperor_string_find_from(const char* s, const char* sub, long long start);
char* _emperor_string_substring(const char* s, long long start, long long length);
char* _emperor_string_char_at(const char* s, long long index);
long long _emperor_string_char_code(const char* s);
long long _emperor_string_to_int(const char* s);

/* Command-line arguments */
long long _emperor_args_count(void);
char* _emperor_args_get(long long index);

/* Execute a command */
long long _emperor_exec_cmd(const char* cmd);

/* File I/O */
char* _emperor_file_read_text(const char* path);
void _emperor_file_write_text(const char* path, const char* text);

/* Filesystem */
char _emperor_mkdir(const char* path);

/* StringBuilder */
void* _emperor_stringbuilder_new(void);
void _emperor_stringbuilder_append(void* sb, const char* s);
char* _emperor_stringbuilder_to_string(void* sb);

#endif
