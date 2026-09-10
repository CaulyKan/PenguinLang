#ifndef EMPEROR_BUILTIN_H
#define EMPEROR_BUILTIN_H

#include "emperor_string.h"

/* Every string in/out of the C runtime is a PenguinLang string
 * (header-prefixed block — see emperor_string.h); libc boundaries convert
 * via _emperor_string_to_cstring() / _emperor_string_adopt_cstring(). */

/* Print a string followed by a newline. */
void _emperor_println(_emperor_string* s);

/* Print a string without a newline. */
void _emperor_print(_emperor_string* s);

/* Print to stderr without a newline. */
void _emperor_eprint(_emperor_string* s);

/* Print to stderr followed by a newline. */
void _emperor_eprintln(_emperor_string* s);

/* Exit the program with the given code. */
void _emperor_exit(int code);

/* Allocate a GC-tracked object of the given size. */
void* _emperor_alloc_impl(int size);

/* Convert i32 to a GC-tracked string. */
_emperor_string* _emperor_int_to_string(int value);

/* Convert i64 to a GC-tracked string. */
_emperor_string* _emperor_i64_to_string(long long value);

/* Concatenate two strings into a GC-tracked result. */
_emperor_string* _emperor_string_concat(_emperor_string* a, _emperor_string* b);

/* Convert bool to a GC-tracked string ("true"/"false"). */
_emperor_string* _emperor_bool_to_string(char value);

/* Convert double to a GC-tracked string. */
_emperor_string* _emperor_double_to_string(double value);

/* Bitwise left shift. */
long long _emperor_lshift(long long value, long long shift);

/* Bitwise right shift. */
long long _emperor_rshift(long long value, long long shift);

/* String helper functions */
long long _emperor_string_length(_emperor_string* s);
int _emperor_string_equal(_emperor_string* a, _emperor_string* b);
long long _emperor_string_find(_emperor_string* s, _emperor_string* sub);
long long _emperor_string_find_from(_emperor_string* s, _emperor_string* sub, long long start);
_emperor_string* _emperor_string_substring(_emperor_string* s, long long start, long long length);
_emperor_string* _emperor_string_char_at(_emperor_string* s, long long index);
long long _emperor_string_char_code(_emperor_string* s);
long long _emperor_string_to_int(_emperor_string* s);
int _emperor_string_starts_with_at(_emperor_string* s, long long at, _emperor_string* prefix);
long long _emperor_string_char_code_at(_emperor_string* s, long long index);
_emperor_string* _emperor_string_slice(_emperor_string* s, long long start, long long length);
double _emperor_string_to_double(_emperor_string* s);

/* Command-line arguments */
long long _emperor_args_count(void);
_emperor_string* _emperor_args_get(long long index);

/* Execute a command */
long long _emperor_exec_cmd(_emperor_string* cmd);

/* Environment */
_emperor_string* _emperor_getenv(_emperor_string* name);

/* File I/O */
_emperor_string* _emperor_file_read_text(_emperor_string* path);
void _emperor_file_write_text(_emperor_string* path, _emperor_string* text);
_emperor_string* _emperor_file_read_range(_emperor_string* path, long long offset, long long size);
long long _emperor_file_size(_emperor_string* path);
void _emperor_file_append(_emperor_string* path, _emperor_string* text);
_emperor_string* _emperor_exe_path(void);

/* Non-blocking fd I/O (scheduler external event sources) */
_emperor_string* _emperor_read_fd(long long fd);
long long _emperor_write_fd(long long fd, _emperor_string* data);

/* Filesystem */
char _emperor_mkdir(_emperor_string* path);
char _emperor_file_exists(_emperor_string* path);
char _emperor_dir_exists(_emperor_string* path);
_emperor_string* _emperor_dir_get_entries(_emperor_string* path);

/* Create and return a fresh, guaranteed-unique temporary directory path
 * (parallel-compile safe). Returns an empty string on failure. */
_emperor_string* _emperor_create_temp_dir(_emperor_string* prefix);

/* Called at program start to increase the stack limit from the default
 * 8 MB to 32 MB, preventing intermittent SIGSEGV from deep recursion
 * in the EmperorPenguin compiler's semantic analysis passes. */
void _emperor_boost_stack(void);

/* StringBuilder */
void _emperor_StringBuilder_new(void* sb);
void _emperor_StringBuilder_append(void* sb, _emperor_string* s);
_emperor_string* _emperor_StringBuilder_to_string(void* sb);

#endif
