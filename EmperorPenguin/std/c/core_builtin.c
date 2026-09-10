#include "emperor_builtin.h"
#include "emperor_gc.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <sys/stat.h>
#include <sys/types.h>
#ifdef _WIN32
#include <windows.h>
#include <direct.h>
#include <process.h>
#include <io.h>
#include <fcntl.h>
#else
#include <dirent.h>
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <sys/resource.h>
#endif

/* Debug asserts (Phase 3.4). Compile the C runtime with -DEMPEROR_DEBUG (e.g.
 * `make CFLAGS=-DEMPEROR_DEBUG`) to enable. The default build (used by the
 * test suite) leaves these out, so there is zero impact on E2E tests. The
 * asserts catch the most common native value-type/codegen failure modes early
 * — NULL pointers passed to runtime helpers, and failed GC allocations — which
 * otherwise manifest as opaque segfaults deep in the bootstrap. */

/* Increase the stack limit at load time. The self-hosted EmperorPenguin
 * compiler uses deep recursion in the semantic analysis passes (especially
 * pass_build_scopes), which can overflow the default 8 MB stack limit on
 * Linux and cause intermittent SIGSEGV. 32 MB gives a generous safety margin
 * without consuming significant memory (stack pages are committed on demand).
 *
 * Windows has no POSIX rlimits: the runtime stack size is fixed by the PE
 * header (set at link time with `-Wl,--stack,N` — see main.penguin), so on
 * Windows this function is a no-op. `__attribute__((constructor))` is accepted
 * by clang on all targets (llvm-mingw) but not by MSVC, hence the guard. */
#if !defined(_WIN32) || defined(__clang__)
__attribute__((constructor))
static void emperor_boost_stack_ctor(void) {
    _emperor_boost_stack();
}
#endif

void _emperor_boost_stack(void) {
#ifndef _WIN32
    struct rlimit rl;
    if (getrlimit(RLIMIT_STACK, &rl) == 0) {
        if (rl.rlim_cur < 32 * 1024 * 1024) {
            rl.rlim_cur = 32 * 1024 * 1024;
            if (rl.rlim_max < 32 * 1024 * 1024)
                rl.rlim_max = 32 * 1024 * 1024;
            setrlimit(RLIMIT_STACK, &rl);
        }
    }
#endif
}
#ifdef EMPEROR_DEBUG
#include <assert.h>
#define EMPEROR_ASSERT(cond, msg) do { if (!(cond)) { fprintf(stderr, "emperor assert: %s (%s:%d)\n", msg, __FILE__, __LINE__); assert(cond); } } while (0)
#else
#define EMPEROR_ASSERT(cond, msg) do { } while (0)
#endif

/* M6-step2 object bridge: recover a live object from an object_ref address (a
 * compile-time value-template object argument's address in this process's GC
 * heap). Pure inttoptr. Called from JIT'd #fun bodies via the meta_extern_decls
 * extern `penguin_meta_get_object` in namespace emperor (mapped LITERAL to this
 * emperor_-prefixed symbol, matching the MetaHost responder convention — NOT
 * the _emperor_ builtin prefix). Implemented in C (not MetaHost.penguin)
 * because a PenguinLang body would need unsafe_cast, which pass1's
 * BabyPenguin/ANTLR parser does not accept. The #fun body casts the returned
 * reference down to the concrete type before use. */
void* emperor_penguin_meta_get_object(long long ref) {
    return (void *)(intptr_t)ref;
}

/* --- String core (emperor_string.h) --- */

/* The string class metadata (see emperor_string.h). interface_count == 0, no
 * vtable, no destructor: `x is IFace` on a string reads this and cleanly
 * reports false (with the old bare char* repr that only worked by luck —
 * fresh is_string GC blocks happened to be zeroed, so the metaptr slot read
 * as NULL). Exported with the executable via -rdynamic so JIT unit-B modules
 * and .penguin-lib consumers resolve their literal references against it. */
EmperorClassMetadata _emperor_string_metadata = {
    "string", /* name */
    0,        /* instance_size */
    0,        /* field_count */
    NULL,     /* field_offsets */
    NULL,     /* field_is_ptr */
    NULL,     /* virtual_method_table */
    0,        /* interface_count */
    NULL,     /* interface_map */
    NULL,     /* destructor */
};

_emperor_string* _emperor_string_alloc(int64_t len) {
    if (len < 0) len = 0;
    /* is_string=1 keeps the block opaque to the GC (never scanned, never
     * finalized), so the metaptr stamped below is invisible to collection. */
    _emperor_string* s = (_emperor_string*)_emperor_gc_alloc(
        (int)(EMPEROR_STRING_HEADER_SIZE + (size_t)len + 1), 1);
    if (!s) return NULL;
    s->metaptr = (void*)&_emperor_string_metadata;
    s->length = len;
    s->data[len] = '\0';
    return s;
}

_emperor_string* _emperor_string_adopt_cstring(const char* c) {
    if (!c) return _emperor_string_alloc(0);
    size_t len = strlen(c);
    _emperor_string* s = _emperor_string_alloc((int64_t)len);
    if (!s) return NULL;
    if (len) memcpy(s->data, c, len);
    return s;
}

/* Clamp a snprintf return value into the string header: n < 0 → empty
 * (encoding error); n > cap → truncated (snprintf already NUL-terminated
 * data[cap]); otherwise the exact formatted length. */
static void emperor_string_finish_format(_emperor_string* s, int n, int cap) {
    if (n < 0) {
        s->data[0] = '\0';
        s->length = 0;
    } else if (n <= cap) {
        s->length = n;
    } else {
        s->length = cap;
    }
}

/* Portable memmem (glibc needs _GNU_SOURCE for the declaration and mingw's
 * availability is spotty). Same complexity class; find() is no longer on any
 * O(n^2) hot path now that lengths are O(1). Returns NULL when absent, the
 * haystack itself for an empty needle. */
static const char* emperor_memmem(const char* haystack, size_t haystacklen,
                                  const char* needle, size_t needlelen) {
    if (needlelen == 0) return haystack;
    if (haystacklen < needlelen) return NULL;
    for (size_t i = 0; i + needlelen <= haystacklen; i++) {
        if (haystack[i] == needle[0] && memcmp(haystack + i, needle, needlelen) == 0) {
            return haystack + i;
        }
    }
    return NULL;
}

/* --- I/O --- */

void _emperor_println(_emperor_string* s) {
    if (s) {
        fwrite(s->data, 1, (size_t)s->length, stdout);
    }
    fputc('\n', stdout);
    fflush(stdout);
}

void _emperor_print(_emperor_string* s) {
    if (s) {
        fwrite(s->data, 1, (size_t)s->length, stdout);
    }
    fflush(stdout);
}

void _emperor_eprint(_emperor_string* s) {
    if (s) {
        fwrite(s->data, 1, (size_t)s->length, stderr);
    }
    fflush(stderr);
}

void _emperor_eprintln(_emperor_string* s) {
    if (s) {
        fwrite(s->data, 1, (size_t)s->length, stderr);
    }
    fputc('\n', stderr);
    fflush(stderr);
}

void _emperor_exit(int code) {
    /* exit() inside a coroutine (initial routine / async spawn) must unwind
     * to the scheduler instead of terminating the process mid-switch: the
     * scheduler ends the program with this code after flushing its loop.
     * _emperor_gc_on_coroutine (scheduler.c) is non-zero exactly while a
     * coroutine is running; zero for plain programs → direct exit. */
    {
        extern int _emperor_gc_on_coroutine;
        extern void _emperor_sched_exit(int);
        if (_emperor_gc_on_coroutine) {
            _emperor_sched_exit(code);
            return;
        }
    }
    exit(code);
}

/* --- Allocation --- */

void* _emperor_alloc_impl(int size) {
    EMPEROR_ASSERT(size > 0, "_emperor_alloc_impl: size must be positive");
    return _emperor_gc_alloc(size, 0);
}

/* --- Raw heap allocation for manually-managed buffers (Array<T,N>, _ptr<T>).
 * Wraps libc malloc/free directly (NOT the GC arena) — buffer lifetime is
 * explicitly controlled by IMemoryDispose.dispose_mem(). The raw address is
 * returned as an i64 (u64 in PenguinLang); the GC never touches this memory. */
int64_t _emperor_malloc(int64_t size) {
    EMPEROR_ASSERT(size > 0, "_emperor_malloc: size must be positive");
    void* p = malloc((size_t)size);
    EMPEROR_ASSERT(p != NULL, "_emperor_malloc: allocation failed");
    return (int64_t)(uintptr_t)p;
}

void _emperor_mfree(int64_t addr) {
    if (addr != 0) {
        free((void*)(uintptr_t)addr);
    }
}

/* --- Byte-level helpers for generic HashMap<K,V> (value-type keys).
 * _hash_bytes: FNV-1a over len bytes at addr. _bytes_equal: memcmp. The address
 * args come from #__address_of(k) (a copy holding k's value) and buffer arithmetic. */
int64_t _emperor_hash_bytes(int64_t addr, int64_t len) {
    const uint8_t* p = (const uint8_t*)(uintptr_t)addr;
    uint64_t h = 14695981039346656037ULL; /* FNV-1a offset basis */
    for (int64_t i = 0; i < len; i++) {
        h ^= (uint64_t)p[i];
        h *= 1099511628211ULL; /* FNV prime */
    }
    return (int64_t)h;
}

int64_t _emperor_bytes_equal(int64_t addr1, int64_t addr2, int64_t len) {
    if (len <= 0) { return 1; }
    return memcmp((const void*)(uintptr_t)addr1, (const void*)(uintptr_t)addr2, (size_t)len) == 0 ? 1 : 0;
}

/* --- Conversions --- */

_emperor_string* _emperor_int_to_string(int value) {
    _emperor_string* s = _emperor_string_alloc(31);
    if (!s) return NULL;
    emperor_string_finish_format(s, snprintf(s->data, 32, "%d", value), 31);
    return s;
}

_emperor_string* _emperor_i64_to_string(long long value) {
    _emperor_string* s = _emperor_string_alloc(31);
    if (!s) return NULL;
    emperor_string_finish_format(s, snprintf(s->data, 32, "%lld", value), 31);
    return s;
}

_emperor_string* _emperor_string_concat(_emperor_string* a, _emperor_string* b) {
    EMPEROR_ASSERT(a != NULL, "_emperor_string_concat: NULL first argument");
    EMPEROR_ASSERT(b != NULL, "_emperor_string_concat: NULL second argument");
    int64_t la = a ? a->length : 0;
    int64_t lb = b ? b->length : 0;
    _emperor_string* result = _emperor_string_alloc(la + lb);
    if (result) {
        if (la) memcpy(result->data, a->data, (size_t)la);
        if (lb) memcpy(result->data + la, b->data, (size_t)lb);
    }
    return result;
}

/* Content-based string equality. PenguinLang `==`/`!=` on strings must compare
 * the character contents, not the char* pointers — every string literal is a
 * distinct global and every substring/concat is a fresh GC allocation, so a
 * pointer comparison (`icmp eq ptr`) is almost always false even for equal
 * text (e.g. the lexer's `substring(source,pos,len) == "namespace"` keyword
 * check, which otherwise never matches and leaves every keyword token as an
 * Identifier). Length first (O(1) reject), then memcmp. Returns 1 if the
 * contents are equal, 0 otherwise. */
int _emperor_string_equal(_emperor_string* a, _emperor_string* b) {
    if (a == b) return 1;
    if (!a || !b) return 0;
    if (a->length != b->length) return 0;
    return memcmp(a->data, b->data, (size_t)a->length) == 0 ? 1 : 0;
}

/* Lexicographic (byte-wise, strcmp semantics) ordering for PenguinLang's
 * relational operators on strings (`<`, `>`, `<=`, `>=`). The emitter lowers
 * those to `icmp <pred> i32 (call _emperor_string_compare(a,b)), 0` — a raw
 * pointer icmp would compare literal/heap ADDRESSES instead of contents.
 * Returns <0, 0 or >0 exactly like strcmp. */
int _emperor_string_compare(_emperor_string* a, _emperor_string* b) {
    if (a == b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    size_t n = (size_t)(a->length < b->length ? a->length : b->length);
    int r = n ? memcmp(a->data, b->data, n) : 0;
    if (r != 0) return r < 0 ? -1 : 1;
    if (a->length == b->length) return 0;
    return a->length < b->length ? -1 : 1;
}

_emperor_string* _emperor_bool_to_string(char value) {
    _emperor_string* s = _emperor_string_alloc(5);
    if (!s) return NULL;
    if (value) {
        memcpy(s->data, "true", 4);
        s->length = 4;
        s->data[4] = '\0';
    } else {
        memcpy(s->data, "false", 5);
        s->length = 5;
        s->data[5] = '\0'; /* data[5] is the terminator slot of alloc(5) */
    }
    return s;
}

_emperor_string* _emperor_double_to_string(double value) {
    _emperor_string* s = _emperor_string_alloc(63);
    if (!s) return NULL;
    emperor_string_finish_format(s, snprintf(s->data, 64, "%g", value), 63);
    return s;
}

/* --- Bitwise --- */

long long _emperor_lshift(long long value, long long shift) {
    return value << shift;
}

long long _emperor_rshift(long long value, long long shift) {
    return value >> shift;
}

/* --- String helpers --- */

long long _emperor_string_length(_emperor_string* s) {
    if (!s) return 0;
    return (long long)s->length;
}

long long _emperor_string_find(_emperor_string* s, _emperor_string* sub) {
    if (!s || !sub) return -1;
    const char* p = emperor_memmem(s->data, (size_t)s->length,
                                   sub->data, (size_t)sub->length);
    if (!p) return -1;
    return (long long)(p - s->data);
}

long long _emperor_string_find_from(_emperor_string* s, _emperor_string* sub, long long start) {
    if (!s || !sub) return -1;
    if (start < 0 || start >= s->length) return -1;
    if (sub->length == 0) return start;
    const char* p = emperor_memmem(s->data + start, (size_t)(s->length - start),
                                   sub->data, (size_t)sub->length);
    if (!p) return -1;
    return (long long)(p - s->data);
}

_emperor_string* _emperor_string_substring(_emperor_string* s, long long start, long long length) {
    if (!s) {
        return _emperor_string_alloc(0);
    }
    long long slen = (long long)s->length;
    if (start < 0) start = 0;
    if (start > slen) start = slen;
    if (length < 0) length = 0;
    if (start + length > slen) length = slen - start;
    _emperor_string* result = _emperor_string_alloc(length);
    if (result) {
        memcpy(result->data, s->data + start, (size_t)length);
    }
    return result;
}

_emperor_string* _emperor_string_char_at(_emperor_string* s, long long index) {
    _emperor_string* result = _emperor_string_alloc(1);
    if (!result) return NULL;
    if (s && index >= 0 && index < (long long)s->length) {
        result->data[0] = s->data[index];
    } else {
        result->data[0] = '\0';
        result->length = 0;
    }
    return result;
}

long long _emperor_string_char_code(_emperor_string* s) {
    if (!s || s->length == 0) return -1;
    return (long long)(unsigned char)s->data[0];
}

/* O(prefix) byte compare with NO allocation: the lexer calls this up to ~60
 * times per source position (keyword chain). Bounds come from the header in
 * O(1); an out-of-range `at` (at + prefix->length > length) rejects. */
int _emperor_string_starts_with_at(_emperor_string* s, long long at, _emperor_string* prefix) {
    if (!s || !prefix || at < 0) return 0;
    if (at + (long long)prefix->length > (long long)s->length) return 0;
    return memcmp(s->data + at, prefix->data, (size_t)prefix->length) == 0 ? 1 : 0;
}

/* O(1) code-unit read (same contract: callers keep the index in [0, length];
 * the index at/after the length — the old NUL sentinel position — returns -1). */
long long _emperor_string_char_code_at(_emperor_string* s, long long index) {
    if (!s || index < 0 || index >= (long long)s->length) return -1;
    return (long long)(unsigned char)s->data[index];
}

/* Trusted-bounds substring: NO clamping — the caller has cached the length and
 * checked [start, start+length) itself (json reader segment copies, lexer
 * token slices on the cached source_len). */
_emperor_string* _emperor_string_slice(_emperor_string* s, long long start, long long length) {
    if (!s || start < 0 || length < 0) {
        return _emperor_string_alloc(0);
    }
    _emperor_string* result = _emperor_string_alloc(length);
    if (result) {
        memcpy(result->data, s->data + start, (size_t)length);
    }
    return result;
}

long long _emperor_string_to_int(_emperor_string* s) {
    if (!s) return 0;
    return atoll(s->data);
}

double _emperor_string_to_double(_emperor_string* s) {
    if (!s) return 0.0;
    return strtod(s->data, NULL);
}

/* --- Command-line args --- */

static int g_argc = 0;
static char** g_argv = NULL;

/* Called from main() to store argc/argv.
 * Skips argv[0] (the program name) so that __builtin.args() returns only the
 * user-supplied arguments — matching the BabyPenguin VM, where CommandLineArgs
 * is set to the tokens after the "--" separator (Program.cs) and never includes
 * a program name. Without this, a native EmperorPenguin binary would treat its
 * own path (argv[0]) as the first source file and try to compile itself. */
void _emperor_args_init(int argc, char** argv) {
    if (argc > 0 && argv != NULL) {
        g_argc = argc - 1;
        g_argv = argv + 1;
    } else {
        g_argc = 0;
        g_argv = NULL;
    }
}

long long _emperor_args_count(void) {
    return (long long)g_argc;
}

_emperor_string* _emperor_args_get(long long index) {
    if (index < 0 || index >= g_argc || !g_argv) {
        return _emperor_string_alloc(0);
    }
    long long len = (long long)strlen(g_argv[index]);
    _emperor_string* result = _emperor_string_alloc(len);
    if (result) {
        memcpy(result->data, g_argv[index], (size_t)len);
    }
    return result;
}

/* --- Exec --- */

long long _emperor_exec_cmd(_emperor_string* cmd) {
    if (!cmd) return -1;
    return (long long)system(cmd->data);
}

/* --- Environment --- */

/* "" when unset (GC-allocated, so the caller gets a stable penguin string).
 * Resolves tool paths (e.g. $CLANG) in-process — a `${VAR:-def}` shell
 * expansion only works under a POSIX system() shell, not cmd.exe. */
_emperor_string* _emperor_getenv(_emperor_string* name) {
    const char* v = (name && name->length) ? getenv(name->data) : NULL;
    size_t len = v ? strlen(v) : 0;
    _emperor_string* r = _emperor_string_alloc((int64_t)len);
    if (r && len) {
        memcpy(r->data, v, len);
    }
    return r;
}

/* --- File I/O --- */

_emperor_string* _emperor_file_read_text(_emperor_string* path) {
    if (!path) {
        return _emperor_string_alloc(0);
    }
    FILE* f = fopen(path->data, "r");
    if (!f) {
        return _emperor_string_alloc(0);
    }
    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);
    if (size < 0) size = 0;
    _emperor_string* buf = _emperor_string_alloc(size);
    if (buf) {
        size_t got = fread(buf->data, 1, (size_t)size, f);
        buf->length = (int64_t)got;
        buf->data[got] = '\0';
    }
    fclose(f);
    return buf;
}

void _emperor_file_write_text(_emperor_string* path, _emperor_string* text) {
    if (!path) return;
    FILE* f = fopen(path->data, "w");
    if (!f) return;
    if (text) {
        fwrite(text->data, 1, (size_t)text->length, f);
    }
    fclose(f);
}

/* --- Non-blocking fd I/O (external event sources; scheduler.c) ---
 * Single-syscall helpers for the coroutine fd protocol: the caller parks on
 * _emperor_fd_wait_read/_write between calls, so these never (need to) block
 * the scheduler thread. POSIX: descriptors are switched to O_NONBLOCK on
 * first use — a plain blocking write to a full pipe would freeze every
 * coroutine.
 *
 * _emperor_read_fd: one read() of up to 32KB into a fresh GC string; a
 * zero-length result means EOF-after-wake (level-triggered poll only wakes
 * an empty reader for data or HUP, and read-after-HUP returns 0) or a
 * spurious EAGAIN (the next wait simply re-parks).
 * _emperor_write_fd: one write() of the string's bytes (header length, so
 * binary frames with embedded NULs go out whole); returns the count,
 * 0 for EAGAIN (caller parks on fd_wait_write and retries), -1 on error.
 *
 * Windows: there is no O_NONBLOCK for pipes; the model is wait-then-syscall
 * (scheduler.c's fd integration only wakes a reader after PeekNamedPipe
 * reports data/broken, so the blocking read returns immediately) plus a
 * BLOCKING write — when the pipe is full, the write itself is the
 * backpressure, which is equivalent for the LSP's strictly-ordered single
 * writer. _O_BINARY stops the CRT from translating CRLF/LF (frames are
 * byte-exact protocol payloads). */
#define EMPEROR_FD_CHUNK (32 * 1024)

static void emperor_fd_set_nonblock(int fd) {
#ifndef _WIN32
    int flags = fcntl(fd, F_GETFL, 0);
    if (flags >= 0 && !(flags & O_NONBLOCK)) {
        fcntl(fd, F_SETFL, flags | O_NONBLOCK);
    }
#else
    (void)fd;
#endif
}

_emperor_string* _emperor_read_fd(long long fd) {
    _emperor_string* buf = _emperor_string_alloc(EMPEROR_FD_CHUNK);
    if (!buf) {
        return _emperor_string_alloc(0);
    }
#ifdef _WIN32
    int fdi = (int)fd;
    emperor_fd_set_nonblock(fdi); /* no-op: wait-then-syscall model */
    _setmode(fdi, _O_BINARY);
    int n = read(fdi, buf->data, EMPEROR_FD_CHUNK);
    if (n < 0) n = 0; /* error: deliver "" (EOF-shaped; caller ends or re-parks) */
#else
    emperor_fd_set_nonblock((int)fd);
    ssize_t n = read((int)fd, buf->data, EMPEROR_FD_CHUNK);
    if (n < 0) n = 0; /* EAGAIN et al.: deliver "" */
#endif
    buf->length = (int64_t)n;
    buf->data[n] = '\0';
    return buf;
}

long long _emperor_write_fd(long long fd, _emperor_string* data) {
    if (!data) return 0;
    const char* bytes = data->data;
    size_t len = (size_t)data->length;
#ifdef _WIN32
    int fdi = (int)fd;
    emperor_fd_set_nonblock(fdi); /* no-op: blocking write IS the backpressure */
    _setmode(fdi, _O_BINARY);
    size_t off = 0;
    while (off < len) {
        size_t piece = len - off;
        if (piece > 0x40000000u) piece = 0x40000000u; /* _write takes unsigned */
        int n = write(fdi, bytes + off, (unsigned)piece);
        if (n < 0) {
            return (off > 0) ? (long long)off : -1;
        }
        if (n == 0) break; /* shouldn't happen for a blocking write */
        off += (size_t)n;
    }
    return (long long)off;
#else
    emperor_fd_set_nonblock((int)fd);
    ssize_t n = write((int)fd, bytes, len);
    if (n < 0) {
        return (errno == EAGAIN || errno == EWOULDBLOCK) ? 0 : -1;
    }
    return (long long)n;
#endif
}

/* --- Binary file I/O (dynamic-linking support) ---
 * These back the `_utils.file_size` / `file_read_range` / `file_append` /
 * `exe_path` externs. The read/append paths MUST use binary mode ("rb"/"ab"):
 * on Windows text mode translates CRLF and would corrupt a .so/.dll tail + the
 * appended JSON metadata + the PENGUINLIB footer. The header now carries the
 * exact byte count, so file_read_range results keep embedded NULs intact (the
 * metadata is ASCII; the DynlibStub footer scan only reads the tail). */

long long _emperor_file_size(_emperor_string* path) {
    if (!path) return -1;
    struct stat st;
    if (stat(path->data, &st) != 0) return -1;
    return (long long)st.st_size;
}

/* Reads `size` bytes at byte offset `offset` into a GC string (length = bytes
 * actually read). Returns an empty string on error or when the range exceeds
 * the file. */
_emperor_string* _emperor_file_read_range(_emperor_string* path, long long offset, long long size) {
    _emperor_string* empty = _emperor_string_alloc(0);
    if (!path || offset < 0 || size < 0) return empty;
    FILE* f = fopen(path->data, "rb");
    if (!f) return empty;
    if (fseek(f, (long)offset, SEEK_SET) != 0) { fclose(f); return empty; }
    _emperor_string* buf = _emperor_string_alloc(size);
    if (buf) {
        size_t got = fread(buf->data, 1, (size_t)size, f);
        buf->length = (int64_t)got;
        buf->data[got] = '\0';
    }
    fclose(f);
    return buf ? buf : empty;
}

void _emperor_file_append(_emperor_string* path, _emperor_string* text) {
    if (!path) return;
    FILE* f = fopen(path->data, "ab");
    if (!f) return;
    if (text) {
        fwrite(text->data, 1, (size_t)text->length, f);
    }
    fclose(f);
}

/* Path of the running compiler executable. Used to locate .penguin-lib files
 * in the compiler's own directory. Windows: GetModuleFileNameA; Linux/BSD:
 * readlink /proc/self/exe; future macOS: _NSGetExecutablePath. */
_emperor_string* _emperor_exe_path(void) {
#ifdef _WIN32
    DWORD buf_size = 8192;
    _emperor_string* s = _emperor_string_alloc((int64_t)buf_size - 1);
    if (!s) return s;
    DWORD got = GetModuleFileNameA(NULL, s->data, buf_size);
    if (got == 0 || got >= buf_size) {
        s->length = 0;
        s->data[0] = '\0';
    } else {
        s->length = (int64_t)got;
        s->data[got] = '\0';
    }
    return s;
#else
    int64_t cap = 4095;
    _emperor_string* s = _emperor_string_alloc(cap);
    if (!s) return s;
    long got = (long)readlink("/proc/self/exe", s->data, (size_t)cap);
    if (got < 0) {
        s->length = 0;
        s->data[0] = '\0';
    } else {
        s->length = (int64_t)got; /* readlink does not NUL-terminate */
        s->data[got] = '\0';
    }
    return s;
#endif
}

/* --- Filesystem --- */

char _emperor_mkdir(_emperor_string* path) {
    if (!path) return 0;
#ifdef _WIN32
    int ret = _mkdir(path->data);
#else
    int ret = mkdir(path->data, 0755);
#endif
    return (ret == 0) ? 1 : 0;
}

/* --- Per-process temp directory (parallel-compile safe) --- */

/* Creates a fresh, guaranteed-unique directory under the system temp area and
 * returns its path (GC-tracked). On POSIX this uses mkdtemp, which creates the
 * directory atomically; on Windows a candidate name is built from pid + tick +
 * counter and mkdir is retried until it succeeds. Either way two parallel
 * callers can never receive the same path, so build intermediates placed here
 * do not collide across concurrent compiler invocations. Returns an empty
 * string on failure. */
_emperor_string* _emperor_create_temp_dir(_emperor_string* prefix) {
    const char* pfx = (prefix && prefix->length) ? prefix->data : "penguin";

    /* Resolve the base temp directory. */
    char base_buf[1100];
    const char* base;
#ifdef _WIN32
    DWORD got = GetTempPathA(sizeof(base_buf), base_buf);
    if (got == 0 || got >= sizeof(base_buf)) { base_buf[0] = '.'; base_buf[1] = '\0'; }
    else { base_buf[got] = '\0'; }
    base = base_buf;
#else
    const char* tdir = getenv("TMPDIR");
    if (tdir && tdir[0]) {
        size_t tl = strlen(tdir);
        if (tl >= sizeof(base_buf)) tl = sizeof(base_buf) - 1;
        memcpy(base_buf, tdir, tl);
        base_buf[tl] = '\0';
        base = base_buf;
    } else {
        base = "/tmp";
    }
#endif

    size_t blen = strlen(base);
    int base_has_sep = (blen > 0 && (base[blen - 1] == '/' || base[blen - 1] == '\\'));

#ifndef _WIN32
    /* POSIX: mkdtemp atomically creates the directory, guaranteeing uniqueness
     * even under concurrent callers. Retry a bounded number of times. */
    for (int attempt = 0; attempt < 256; attempt++) {
        char tmpl[4096];
        int n = snprintf(tmpl, sizeof(tmpl), "%s%s%s_%ld_%dXXXXXX",
                         base, base_has_sep ? "" : "/", pfx, (long)getpid(), attempt);
        if (n <= 0 || (size_t)n >= sizeof(tmpl)) break;
        if (mkdtemp(tmpl) != NULL) {
            return _emperor_string_adopt_cstring(tmpl);
        }
        /* EEXIST or transient failure: retry with a fresh suffix. */
    }
#else
    /* Windows: loop building candidate names; _mkdir succeeds on the first one
     * that does not yet exist, which is atomic with respect to creation. */
    for (int attempt = 0; attempt < 256; attempt++) {
        char path[4096];
        unsigned long pid = (unsigned long)GetCurrentProcessId();
        unsigned long tick = (unsigned long)(GetTickCount() + (unsigned long)attempt);
        int n = snprintf(path, sizeof(path), "%s%s%s_%lu_%lu_%d",
                         base, base_has_sep ? "" : "\\", pfx, pid, tick, attempt);
        if (n <= 0 || (size_t)n >= sizeof(path)) break;
        if (_mkdir(path) == 0) {
            return _emperor_string_adopt_cstring(path);
        }
    }
#endif

    return _emperor_string_alloc(0);
}

/* --- Filesystem queries --- */

char _emperor_file_exists(_emperor_string* path) {
    if (!path) return 0;
#ifdef _WIN32
    struct _stat st;
    if (_stat(path->data, &st) != 0) return 0;
    return (st.st_mode & _S_IFREG) ? 1 : 0;
#else
    struct stat st;
    return (stat(path->data, &st) == 0 && S_ISREG(st.st_mode)) ? 1 : 0;
#endif
}

char _emperor_dir_exists(_emperor_string* path) {
    if (!path) return 0;
#ifdef _WIN32
    struct _stat st;
    if (_stat(path->data, &st) != 0) return 0;
    return (st.st_mode & _S_IFDIR) ? 1 : 0;
#else
    struct stat st;
    return (stat(path->data, &st) == 0 && S_ISDIR(st.st_mode)) ? 1 : 0;
#endif
}

_emperor_string* _emperor_dir_get_entries(_emperor_string* path) {
    if (!path) {
        return _emperor_string_alloc(0);
    }

#ifdef _WIN32
    /* Build search pattern: path + "\\*" */
    int pathlen = (int)path->length;
    char* pattern = (char*)malloc((size_t)pathlen + 3);
    if (!pattern) {
        return _emperor_string_alloc(0);
    }
    memcpy(pattern, path->data, (size_t)pathlen);
    pattern[pathlen] = '\\';
    pattern[pathlen + 1] = '*';
    pattern[pathlen + 2] = '\0';

    WIN32_FIND_DATAA findData;
    HANDLE hFind = FindFirstFileA(pattern, &findData);
    if (hFind == INVALID_HANDLE_VALUE) {
        free(pattern);
        return _emperor_string_alloc(0);
    }

    /* First pass: calculate total length */
    int total = 0;
    int count = 0;
    do {
        const char* name = findData.cFileName;
        if (name[0] == '.' && (name[1] == '\0' || (name[1] == '.' && name[2] == '\0'))) {
            continue;
        }
        total += (int)strlen(name);
        count++;
    } while (FindNextFileA(hFind, &findData));
    FindClose(hFind);

    int bufsize = total + (count > 0 ? count - 1 : 0);
    _emperor_string* result = _emperor_string_alloc(bufsize > 0 ? bufsize : 0);
    if (!result) {
        free(pattern);
        return _emperor_string_alloc(0);
    }

    /* Second pass: build the string (reuse same pattern) */
    hFind = FindFirstFileA(pattern, &findData);
    free(pattern);
    if (hFind == INVALID_HANDLE_VALUE) return result;

    int pos = 0;
    int first = 1;
    do {
        const char* name = findData.cFileName;
        if (name[0] == '.' && (name[1] == '\0' || (name[1] == '.' && name[2] == '\0'))) {
            continue;
        }
        if (!first) {
            result->data[pos++] = '\n';
        }
        int nlen = (int)strlen(name);
        memcpy(result->data + pos, name, (size_t)nlen);
        pos += nlen;
        first = 0;
    } while (FindNextFileA(hFind, &findData));
    result->length = pos;
    result->data[pos] = '\0';
    FindClose(hFind);
    return result;
#else
    DIR* d = opendir(path->data);
    if (!d) {
        return _emperor_string_alloc(0);
    }
    /* First pass: calculate total length */
    int total = 0;
    struct dirent* ent;
    int count = 0;
    while ((ent = readdir(d)) != NULL) {
        const char* name = ent->d_name;
        if (name[0] == '.' && (name[1] == '\0' || (name[1] == '.' && name[2] == '\0'))) {
            continue; /* skip "." and ".." */
        }
        total += (int)strlen(name);
        count++;
    }
    closedir(d);

    /* Allocate result buffer: total name chars + (count-1) newlines */
    int bufsize = total + (count > 0 ? count - 1 : 0);
    _emperor_string* result = _emperor_string_alloc(bufsize > 0 ? bufsize : 0);
    if (!result) {
        return _emperor_string_alloc(0);
    }

    /* Second pass: build the string */
    d = opendir(path->data);
    if (!d) return result;
    int pos = 0;
    int first = 1;
    while ((ent = readdir(d)) != NULL) {
        const char* name = ent->d_name;
        if (name[0] == '.' && (name[1] == '\0' || (name[1] == '.' && name[2] == '\0'))) {
            continue;
        }
        if (!first) {
            result->data[pos++] = '\n';
        }
        int nlen = (int)strlen(name);
        memcpy(result->data + pos, name, (size_t)nlen);
        pos += nlen;
        first = 0;
    }
    result->length = pos;
    result->data[pos] = '\0';
    closedir(d);
    return result;
#endif
}

/* --- io standard library (std/penguin/io.penguin) ---
 * Symbol naming follows the UNIVERSAL extern rule: an extern declared in a
 * namespace maps to <dotted name with '.' as '_'>, so std.io.file_open backs
 * onto std_io_file_open here. Applies to user namespaces identically
 * (mylib.foo -> mylib_foo); only __builtin/_utils keep the historical
 * _emperor_<tail> runtime symbols. */

/* Shared line reader for stdin and io.File handles. Reads with fgetc (at most
 * one byte past the last line) so the feof()-based EOF protocol on the
 * PenguinLang side is exact: eof() is consulted BEFORE a read (stream already
 * exhausted) and AGAIN when a read returned an empty string — a final line
 * without a trailing newline reads back non-empty and sets eof, so the NEXT
 * call's pre-check reports none instead of losing that line. '\r' is dropped
 * everywhere for CRLF tolerance. Returns a GC-allocated string; growth keeps
 * every intermediate buffer a valid _emperor_string (header synced at the
 * end). */
static _emperor_string* io_read_line_stream(FILE* f) {
    if (!f) {
        return _emperor_string_alloc(0);
    }
    size_t cap = 128, len = 0;
    _emperor_string* s = _emperor_string_alloc((int64_t)cap);
    if (!s) return s;
    int c;
    while ((c = fgetc(f)) != EOF) {
        if (c == '\n') break;
        if (c == '\r') continue;
        if (len + 2 > cap) {
            cap *= 2;
            _emperor_string* grown = _emperor_string_alloc((int64_t)cap);
            if (!grown) break;
            memcpy(grown->data, s->data, len);
            s = grown;
        }
        s->data[len++] = (char)c;
    }
    s->data[len] = '\0';
    s->length = (int64_t)len;
    return s;
}

/* Whole remaining stream (from the current position) as one GC string. */
static _emperor_string* io_read_all_stream(FILE* f) {
    size_t cap = 4096, len = 0;
    _emperor_string* s = _emperor_string_alloc((int64_t)cap);
    if (!s) return s;
    for (;;) {
        size_t got = fread(s->data + len, 1, cap - len - 1, f);
        len += got;
        if (got == 0) break;
        if (cap - len < 2) {
            cap *= 2;
            _emperor_string* grown = _emperor_string_alloc((int64_t)cap);
            if (!grown) break;
            memcpy(grown->data, s->data, len);
            s = grown;
        }
    }
    s->data[len] = '\0';
    s->length = (int64_t)len;
    return s;
}

/* console / stdin */
_emperor_string* std_io_stdin_read_line(void) {
    return io_read_line_stream(stdin);
}

char std_io_stdin_eof(void) {
    return (char)(feof(stdin) ? 1 : 0);
}

_emperor_string* std_io_stdin_read_all(void) {
    return io_read_all_stream(stdin);
}

/* File handles: FILE* passed through PenguinLang as i64/u64 (0 = invalid). */
long long std_io_file_open(_emperor_string* path, _emperor_string* mode) {
    if (!path || !mode || mode->length == 0) return 0;
    FILE* f = fopen(path->data, mode->data);
    if (!f) return 0;
    return (long long)(intptr_t)f;
}

void std_io_file_close(long long handle) {
    if (handle != 0) {
        fclose((FILE*)(intptr_t)handle);
    }
}

char std_io_file_write(long long handle, _emperor_string* s) {
    if (handle == 0 || !s) return 0;
    return fwrite(s->data, 1, (size_t)s->length, (FILE*)(intptr_t)handle) == (size_t)s->length ? 1 : 0;
}

_emperor_string* std_io_file_read_line(long long handle) {
    if (handle == 0) {
        return _emperor_string_alloc(0);
    }
    return io_read_line_stream((FILE*)(intptr_t)handle);
}

char std_io_file_eof(long long handle) {
    if (handle == 0) return 1;
    return (char)(feof((FILE*)(intptr_t)handle) ? 1 : 0);
}

void std_io_file_flush(long long handle) {
    if (handle != 0) {
        fflush((FILE*)(intptr_t)handle);
    }
}

_emperor_string* std_io_file_read_all(long long handle) {
    if (handle == 0) {
        return _emperor_string_alloc(0);
    }
    return io_read_all_stream((FILE*)(intptr_t)handle);
}

char std_io_file_seek(long long handle, long long pos) {
    if (handle == 0 || pos < 0) return 0;
    return fseek((FILE*)(intptr_t)handle, (long)pos, SEEK_SET) == 0 ? 1 : 0;
}

long long std_io_file_tell(long long handle) {
    if (handle == 0) return -1;
    return (long long)ftell((FILE*)(intptr_t)handle);
}

/* Whole-file / filesystem helpers with real success reporting (the legacy
 * _emperor_file_write_text & co are void and cannot report failure). */
char std_io_file_write_text(_emperor_string* path, _emperor_string* text) {
    if (!path) return 0;
    FILE* f = fopen(path->data, "w");
    if (!f) return 0;
    int ok = 1;
    if (text && text->length) {
        ok = (fwrite(text->data, 1, (size_t)text->length, f) == (size_t)text->length);
    }
    ok = (fclose(f) == 0) && ok;
    return (char)(ok ? 1 : 0);
}

char std_io_file_append_text(_emperor_string* path, _emperor_string* text) {
    if (!path) return 0;
    FILE* f = fopen(path->data, "a");
    if (!f) return 0;
    int ok = 1;
    if (text && text->length) {
        ok = (fwrite(text->data, 1, (size_t)text->length, f) == (size_t)text->length);
    }
    ok = (fclose(f) == 0) && ok;
    return (char)(ok ? 1 : 0);
}

char std_io_file_remove(_emperor_string* path) {
    if (!path) return 0;
    return remove(path->data) == 0 ? 1 : 0;
}

char std_io_file_rename(_emperor_string* from, _emperor_string* to) {
    if (!from || !to) return 0;
    return rename(from->data, to->data) == 0 ? 1 : 0;
}

/* Query helpers reused from the legacy (pre-std.io) runtime entry points.
 * The std.io externs above declare these under their mangled names
 * (std.io.file_read_text -> std_io_file_read_text); the legacy
 * _utils externs of the compiler itself still bind the original symbols, so
 * both names must resolve. Thin aliases — single implementation stays put. */
_emperor_string* std_io_file_read_text(_emperor_string* path) {
    return _emperor_file_read_text(path);
}

char std_io_file_exists(_emperor_string* path) {
    return _emperor_file_exists(path);
}

char std_io_dir_exists(_emperor_string* path) {
    return _emperor_dir_exists(path);
}

long long std_io_file_size(_emperor_string* path) {
    return _emperor_file_size(path);
}

_emperor_string* std_io_dir_get_entries(_emperor_string* path) {
    return _emperor_dir_get_entries(path);
}

char std_io_file_mkdir(_emperor_string* path) {
    return _emperor_mkdir(path);
}

/* --- StringBuilder --- */

/* Layout must match EmperorPenguin's StringBuilder class: a metadata ptr at
 * offset 0 (every EmperorPenguin object has one), then data/len/cap. The
 * PenguinLang class declares `data: string; len: i32; cap: i32;` so the
 * emitter lays out [metadata, data, len, cap] identically. `data` is now a
 * full _emperor_string (the in-place growth buffer): its header length is
 * kept in sync with `len` after every append so transient direct reads of the
 * field (core_builtin.penguin's get_unique_name → umangle_escape(this.data))
 * observe the right extent. */
typedef struct StringBuilder {
    void* metadata;
    _emperor_string* data;
    int len;
    int cap;
} StringBuilder;

/* Initializes the already-allocated object (`this`) in place. EmperorPenguin
 * calls this as `call void @_emperor_StringBuilder_new(ptr %this)` — it does
 * NOT use a return value (the PenguinLang `new` is `mut this`, void return) —
 * so we must fill in the fields of the passed-in object, not allocate a new
 * one (the old `void* ...(void)` factory form was ignored by the caller and
 * left `this` uninitialized). */
void _emperor_StringBuilder_new(void* vsb) {
    if (!vsb) return;
    StringBuilder* sb = (StringBuilder*)vsb;
    sb->cap = 256;
    sb->data = _emperor_string_alloc(sb->cap);
    sb->len = 0;
    if (sb->data) sb->data->length = 0; /* logical extent, NOT the capacity */
    /* Same barrier rationale as append's grow: a safepoint poll can sit
     * between the caller's NEW and this constructor (call-site polls fire
     * before the call), and a minor there can already have promoted the
     * fresh StringBuilder — `data` is a ref field store either way. */
    _emperor_gc_write_barrier(sb, (void**)&sb->data);
}

void _emperor_StringBuilder_append(void* vsb, _emperor_string* s) {
    if (!vsb || !s) return;
    StringBuilder* sb = (StringBuilder*)vsb;
    int slen = (int)s->length;
    while (sb->len + slen + 1 > sb->cap) {
        sb->cap *= 2;
        _emperor_string* newdata = _emperor_string_alloc(sb->cap);
        if (newdata) {
            memcpy(newdata->data, sb->data->data, (size_t)sb->len);
            newdata->length = sb->len;
            newdata->data[sb->len] = '\0';
        }
        sb->data = newdata;
        /* Generational barrier: `data` is a REF field and the StringBuilder
         * may already be old (promoted/pinned) while the fresh string is
         * nursery-young — without remembering the slot, a minor judges the
         * string dead and later appends write into recycled memory. The
         * penguin-side WRMBR path emits this barrier; C must not skip it. */
        _emperor_gc_write_barrier(sb, (void**)&sb->data);
    }
    if (sb->data) {
        memcpy(sb->data->data + sb->len, s->data, (size_t)slen);
        sb->len += slen;
        sb->data->data[sb->len] = '\0';
        sb->data->length = sb->len;
    }
}

// Copy a value-type (ICopy) class instance. The metadata stores the instance
// size at offset 8 (after the name pointer). This is used when the LLVM backend
// emits a call to __builtin.ICopy_copy for value-type copies.
void* _emperor_ICopy_copy(void* this_ptr) {
    void* meta = *(void**)this_ptr;
    int size = *(int*)(meta + 8);
    void* new_obj = _emperor_alloc_impl(size);
    if (new_obj) { memcpy(new_obj, this_ptr, size); }
    return new_obj;
}

_emperor_string* _emperor_StringBuilder_to_string(void* vsb) {
    if (!vsb) {
        return _emperor_string_alloc(0);
    }
    StringBuilder* sb = (StringBuilder*)vsb;
    int len = sb->len;
    _emperor_string* result = _emperor_string_alloc(len);
    if (result) {
        if (sb->data && len) memcpy(result->data, sb->data->data, (size_t)len);
    }
    return result;
}
