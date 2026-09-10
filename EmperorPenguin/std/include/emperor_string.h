#ifndef EMPEROR_STRING_H
#define EMPEROR_STRING_H

/* The PenguinLang string representation (Documentation/23_EmperorPenguinLLVM.md
 * §2.4). A string VALUE is a pointer to this header-prefixed block:
 *
 *   string value = ptr --> [ metaptr (8B) | length (8B) | data[length] | '\0' ]
 *
 *   - metaptr points at _emperor_string_metadata (below) so that interface
 *     checks (`x is IFace`) on a string read valid, quiescent metadata with
 *     interface_count == 0 instead of whatever bytes followed the old bare
 *     char* allocation. The GC never dereferences it: string blocks are
 *     allocated with is_string=1 and are never scanned or finalized.
 *   - length is the byte length WITHOUT the trailing NUL; every runtime entry
 *     point is length-driven (no strlen), which is what makes string_length
 *     O(1) and find/concat/equal safe for embedded NULs.
 *   - data[length] == '\0' is maintained purely as C-interop convenience, so
 *     libc callsites can use _emperor_string_to_cstring() without copying.
 *
 * Literals are LLVM global constants of type { ptr, i64, [len+1 x i8] }
 * initialized with @_emperor_string_metadata — the exact same layout, in
 * static storage. */
#include <stdint.h>
#include <stddef.h>
#include "emperor_types.h"

typedef struct _emperor_string {
    void*   metaptr; /* -> _emperor_string_metadata (opaque to the GC) */
    int64_t length;  /* byte length, not counting the trailing NUL */
    char    data[];  /* length bytes; data[length] == '\0' */
} _emperor_string;

/* metaptr (8) + length (8). Kept explicit so the arithmetic below cannot
 * drift from the struct definition. */
#define EMPEROR_STRING_HEADER_SIZE ((size_t)16)

#ifdef __cplusplus
extern "C" {
#endif

/* The string class metadata global: defined in core_builtin.c, exported with
 * the executable via -rdynamic so JIT unit-B modules and .penguin-lib
 * consumers resolve their literal references against it. No vtable, no
 * destructor, no interfaces — a string is a primitive. */
extern EmperorClassMetadata _emperor_string_metadata;

/* Allocate a GC-tracked string of `len` data bytes (data[len] = '\0',
 * length = len, metaptr stamped). Contents are uninitialized except for the
 * terminator. Returns NULL only on allocation failure. */
_emperor_string* _emperor_string_alloc(int64_t len);

/* Copy a foreign NUL-terminated C string (libc return values, static text)
 * into a fresh PenguinLang string. _emperor_string_from_cstring() is only
 * legal on pointers already known to be the data[] of a _emperor_string —
 * foreign buffers MUST go through this instead. */
_emperor_string* _emperor_string_adopt_cstring(const char* c);

#ifdef __cplusplus
}
#endif

/* data[] sits at byte 16 for every string, literal or heap. */
static inline char* _emperor_string_to_cstring(_emperor_string* s) {
    return (char*)s + EMPEROR_STRING_HEADER_SIZE;
}

static inline const char* _emperor_string_to_cstring_const(const _emperor_string* s) {
    return (const char*)s + EMPEROR_STRING_HEADER_SIZE;
}

/* ONLY for pointers known to be &some_string->data[0] (see adopt above). */
static inline _emperor_string* _emperor_string_from_cstring(void* c) {
    return (_emperor_string*)((char*)c - EMPEROR_STRING_HEADER_SIZE);
}

/* _Static_assert is C11; both supported toolchains (clang, llvm-mingw) default
 * to at least gnu17, but keep the guard so exotic compilers stay quiet. */
#if !defined(__cplusplus) && defined(__STDC_VERSION__) && __STDC_VERSION__ >= 201112L
_Static_assert(sizeof(void*) + sizeof(int64_t) == EMPEROR_STRING_HEADER_SIZE,
               "_emperor_string header must be exactly 16 bytes");
#endif

#endif /* EMPEROR_STRING_H */
