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

/* --- I/O --- */

void _emperor_println(const char *s) {
    if (s) {
        fputs(s, stdout);
    }
    fputc('\n', stdout);
    fflush(stdout);
}

void _emperor_print(const char *s) {
    if (s) {
        fputs(s, stdout);
    }
    fflush(stdout);
}

void _emperor_eprint(const char *s) {
    if (s) {
        fputs(s, stderr);
    }
    fflush(stderr);
}

void _emperor_eprintln(const char *s) {
    if (s) {
        fputs(s, stderr);
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

char* _emperor_int_to_string(int value) {
    char* buf = (char*)_emperor_gc_alloc(32, 1);
    EMPEROR_ASSERT(buf != NULL, "_emperor_int_to_string: allocation failed");
    if (buf) {
        snprintf(buf, 32, "%d", value);
    }
    return buf;
}

char* _emperor_i64_to_string(long long value) {
    char* buf = (char*)_emperor_gc_alloc(32, 1);
    EMPEROR_ASSERT(buf != NULL, "_emperor_i64_to_string: allocation failed");
    if (buf) {
        snprintf(buf, 32, "%lld", value);
    }
    return buf;
}

char* _emperor_string_concat(const char* a, const char* b) {
    EMPEROR_ASSERT(a != NULL, "_emperor_string_concat: NULL first argument");
    EMPEROR_ASSERT(b != NULL, "_emperor_string_concat: NULL second argument");
    int la = a ? strlen(a) : 0;
    int lb = b ? strlen(b) : 0;
    char* result = (char*)_emperor_gc_alloc(la + lb + 1, 1);
    if (result) {
        if (a) memcpy(result, a, la);
        if (b) memcpy(result + la, b, lb);
        result[la + lb] = '\0';
    }
    return result;
}

/* Content-based string equality. PenguinLang `==`/`!=` on strings must compare
 * the character contents, not the char* pointers — every string literal is a
 * distinct global and every substring/concat is a fresh GC allocation, so a
 * pointer comparison (`icmp eq ptr`) is almost always false even for equal
 * text (e.g. the lexer's `substring(source,pos,len) == "namespace"` keyword
 * check, which otherwise never matches and leaves every keyword token as an
 * Identifier). Returns 1 if the contents are equal, 0 otherwise. */
int _emperor_string_equal(const char* a, const char* b) {
    if (a == b) return 1;
    if (!a || !b) return 0;
    return strcmp(a, b) == 0 ? 1 : 0;
}

char* _emperor_bool_to_string(char value) {
    char* result = (char*)_emperor_gc_alloc(6, 1);
    if (result) {
        strcpy(result, value ? "true" : "false");
    }
    return result;
}

char* _emperor_double_to_string(double value) {
    char* buf = (char*)_emperor_gc_alloc(64, 1);
    EMPEROR_ASSERT(buf != NULL, "_emperor_double_to_string: allocation failed");
    if (buf) {
        snprintf(buf, 64, "%g", value);
    }
    return buf;
}

/* --- Bitwise --- */

long long _emperor_lshift(long long value, long long shift) {
    return value << shift;
}

long long _emperor_rshift(long long value, long long shift) {
    return value >> shift;
}

/* --- String helpers --- */

long long _emperor_string_length(const char* s) {
    if (!s) return 0;
    return (long long)strlen(s);
}

long long _emperor_string_find(const char* s, const char* sub) {
    if (!s || !sub) return -1;
    const char* p = strstr(s, sub);
    if (!p) return -1;
    return (long long)(p - s);
}

long long _emperor_string_find_from(const char* s, const char* sub, long long start) {
    if (!s || !sub) return -1;
    long long len = (long long)strlen(s);
    if (start < 0 || start >= len) return -1;
    const char* p = strstr(s + start, sub);
    if (!p) return -1;
    return (long long)(p - s);
}

char* _emperor_string_substring(const char* s, long long start, long long length) {
    if (!s) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    long long slen = (long long)strlen(s);
    if (start < 0) start = 0;
    if (start > slen) start = slen;
    if (length < 0) length = 0;
    if (start + length > slen) length = slen - start;
    char* result = (char*)_emperor_gc_alloc(length + 1, 1);
    if (result) {
        memcpy(result, s + start, length);
        result[length] = '\0';
    }
    return result;
}

char* _emperor_string_char_at(const char* s, long long index) {
    char* result = (char*)_emperor_gc_alloc(2, 1);
    if (result) {
        if (s && index >= 0 && index < (long long)strlen(s)) {
            result[0] = s[index];
        } else {
            result[0] = '\0';
        }
        result[1] = '\0';
    }
    return result;
}

long long _emperor_string_char_code(const char* s) {
    if (!s || !s[0]) return -1;
    return (long long)(unsigned char)s[0];
}

long long _emperor_string_to_int(const char* s) {
    if (!s) return 0;
    return atoll(s);
}

double _emperor_string_to_double(const char* s) {
    if (!s) return 0.0;
    return strtod(s, NULL);
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

char* _emperor_args_get(long long index) {
    if (index < 0 || index >= g_argc || !g_argv) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    long long len = (long long)strlen(g_argv[index]);
    char* result = (char*)_emperor_gc_alloc(len + 1, 1);
    if (result) {
        memcpy(result, g_argv[index], len);
        result[len] = '\0';
    }
    return result;
}

/* --- Exec --- */

long long _emperor_exec_cmd(const char* cmd) {
    if (!cmd) return -1;
    return (long long)system(cmd);
}

/* --- Environment --- */

/* "" when unset (GC-allocated, so the caller gets a stable penguin string).
 * Resolves tool paths (e.g. $CLANG) in-process — a `${VAR:-def}` shell
 * expansion only works under a POSIX system() shell, not cmd.exe. */
char* _emperor_getenv(const char* name) {
    const char* v = (name && name[0]) ? getenv(name) : NULL;
    size_t len = v ? strlen(v) : 0;
    char* r = (char*)_emperor_gc_alloc(len + 1, 1);
    if (r) {
        if (len) memcpy(r, v, len);
        r[len] = '\0';
    }
    return r;
}

/* --- File I/O --- */

char* _emperor_file_read_text(const char* path) {
    if (!path) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    FILE* f = fopen(path, "r");
    if (!f) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);
    char* buf = (char*)_emperor_gc_alloc(size + 1, 1);
    if (buf) {
        fread(buf, 1, size, f);
        buf[size] = '\0';
    }
    fclose(f);
    return buf;
}

void _emperor_file_write_text(const char* path, const char* text) {
    if (!path) return;
    FILE* f = fopen(path, "w");
    if (!f) return;
    if (text) {
        fputs(text, f);
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
 * _emperor_write_fd: one write() of the string's bytes; returns the count,
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

char* _emperor_read_fd(long long fd) {
#ifdef _WIN32
    int fdi = (int)fd;
    emperor_fd_set_nonblock(fdi); /* no-op: wait-then-syscall model */
    _setmode(fdi, _O_BINARY);
    char* buf = (char*)_emperor_gc_alloc(EMPEROR_FD_CHUNK + 1, 1);
    if (!buf) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    int n = read(fdi, buf, EMPEROR_FD_CHUNK);
    if (n < 0) n = 0; /* error: deliver "" (EOF-shaped; caller ends or re-parks) */
    buf[n] = '\0';
    return buf;
#else
    emperor_fd_set_nonblock((int)fd);
    char* buf = (char*)_emperor_gc_alloc(EMPEROR_FD_CHUNK + 1, 1);
    if (!buf) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    ssize_t n = read((int)fd, buf, EMPEROR_FD_CHUNK);
    if (n < 0) n = 0; /* EAGAIN et al.: deliver "" */
    buf[n] = '\0';
    return buf;
#endif
}

long long _emperor_write_fd(long long fd, const char* data) {
#ifdef _WIN32
    if (!data) return 0;
    int fdi = (int)fd;
    emperor_fd_set_nonblock(fdi); /* no-op: blocking write IS the backpressure */
    _setmode(fdi, _O_BINARY);
    size_t len = strlen(data);
    size_t off = 0;
    while (off < len) {
        size_t piece = len - off;
        if (piece > 0x40000000u) piece = 0x40000000u; /* _write takes unsigned */
        int n = write(fdi, data + off, (unsigned)piece);
        if (n < 0) {
            return (off > 0) ? (long long)off : -1;
        }
        if (n == 0) break; /* shouldn't happen for a blocking write */
        off += (size_t)n;
    }
    return (long long)off;
#else
    if (!data) return 0;
    emperor_fd_set_nonblock((int)fd);
    size_t len = strlen(data);
    ssize_t n = write((int)fd, data, len);
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
 * appended JSON metadata + the PENGUINLIB footer. The metadata is ASCII, so the
 * NUL-terminated string contract (strlen) stays valid. */

long long _emperor_file_size(const char* path) {
    if (!path) return -1;
    struct stat st;
    if (stat(path, &st) != 0) return -1;
    return (long long)st.st_size;
}

/* Reads `size` bytes at byte offset `offset` into a GC buffer, NUL-terminated.
 * Returns an empty string on error or when the range exceeds the file. */
char* _emperor_file_read_range(const char* path, long long offset, long long size) {
    char* empty = (char*)_emperor_gc_alloc(1, 1);
    if (empty) empty[0] = '\0';
    if (!path || offset < 0 || size < 0) return empty;
    FILE* f = fopen(path, "rb");
    if (!f) return empty;
    if (fseek(f, (long)offset, SEEK_SET) != 0) { fclose(f); return empty; }
    char* buf = (char*)_emperor_gc_alloc(size + 1, 1);
    if (buf) {
        size_t got = fread(buf, 1, (size_t)size, f);
        buf[got] = '\0';
    }
    fclose(f);
    return buf ? buf : empty;
}

void _emperor_file_append(const char* path, const char* text) {
    if (!path) return;
    FILE* f = fopen(path, "ab");
    if (!f) return;
    if (text) {
        fputs(text, f);
    }
    fclose(f);
}

/* Path of the running compiler executable. Used to locate .penguin-lib files
 * in the compiler's own directory. Windows: GetModuleFileNameA; Linux/BSD:
 * readlink /proc/self/exe; future macOS: _NSGetExecutablePath. */
char* _emperor_exe_path(void) {
#ifdef _WIN32
    DWORD buf_size = 8192;
    char* buf = (char*)_emperor_gc_alloc(buf_size, 1);
    if (!buf) return buf;
    DWORD got = GetModuleFileNameA(NULL, buf, buf_size);
    if (got == 0 || got >= buf_size) { buf[0] = '\0'; }
    else { buf[got] = '\0'; }
    return buf;
#else
    long buf_size = 4096;
    char* buf = (char*)_emperor_gc_alloc(buf_size, 1);
    if (!buf) return buf;
    long got = (long)readlink("/proc/self/exe", buf, (size_t)(buf_size - 1));
    if (got < 0) { buf[0] = '\0'; }
    else { buf[got] = '\0'; }
    return buf;
#endif
}

/* --- Filesystem --- */

char _emperor_mkdir(const char* path) {
    if (!path) return 0;
#ifdef _WIN32
    int ret = _mkdir(path);
#else
    int ret = mkdir(path, 0755);
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
char* _emperor_create_temp_dir(const char* prefix) {
    const char* pfx = (prefix && prefix[0]) ? prefix : "penguin";

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
            size_t pl = strlen(tmpl);
            char* result = (char*)_emperor_gc_alloc((int)(pl + 1), 1);
            if (result) memcpy(result, tmpl, pl + 1);
            return result;
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
            size_t pl = strlen(path);
            char* result = (char*)_emperor_gc_alloc((int)(pl + 1), 1);
            if (result) memcpy(result, path, pl + 1);
            return result;
        }
    }
#endif

    char* r = (char*)_emperor_gc_alloc(1, 1);
    if (r) r[0] = '\0';
    return r;
}

/* --- Filesystem queries --- */

char _emperor_file_exists(const char* path) {
    if (!path) return 0;
#ifdef _WIN32
    struct _stat st;
    if (_stat(path, &st) != 0) return 0;
    return (st.st_mode & _S_IFREG) ? 1 : 0;
#else
    struct stat st;
    return (stat(path, &st) == 0 && S_ISREG(st.st_mode)) ? 1 : 0;
#endif
}

char _emperor_dir_exists(const char* path) {
    if (!path) return 0;
#ifdef _WIN32
    struct _stat st;
    if (_stat(path, &st) != 0) return 0;
    return (st.st_mode & _S_IFDIR) ? 1 : 0;
#else
    struct stat st;
    return (stat(path, &st) == 0 && S_ISDIR(st.st_mode)) ? 1 : 0;
#endif
}

char* _emperor_dir_get_entries(const char* path) {
    if (!path) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }

#ifdef _WIN32
    /* Build search pattern: path + "\\*" */
    int pathlen = (int)strlen(path);
    char* pattern = (char*)malloc(pathlen + 3);
    if (!pattern) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    memcpy(pattern, path, pathlen);
    pattern[pathlen] = '\\';
    pattern[pathlen + 1] = '*';
    pattern[pathlen + 2] = '\0';

    WIN32_FIND_DATAA findData;
    HANDLE hFind = FindFirstFileA(pattern, &findData);
    if (hFind == INVALID_HANDLE_VALUE) {
        free(pattern);
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
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

    int bufsize = total + (count > 0 ? count - 1 : 0) + 1;
    char* result = (char*)_emperor_gc_alloc(bufsize > 0 ? bufsize : 1, 1);
    if (!result) {
        free(pattern);
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    result[0] = '\0';

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
            result[pos++] = '\n';
        }
        int nlen = (int)strlen(name);
        memcpy(result + pos, name, nlen);
        pos += nlen;
        first = 0;
    } while (FindNextFileA(hFind, &findData));
    result[pos] = '\0';
    FindClose(hFind);
    return result;
#else
    DIR* d = opendir(path);
    if (!d) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
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

    /* Allocate result buffer: total name chars + (count-1) newlines + null terminator */
    int bufsize = total + (count > 0 ? count - 1 : 0) + 1;
    char* result = (char*)_emperor_gc_alloc(bufsize > 0 ? bufsize : 1, 1);
    if (!result) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    result[0] = '\0';

    /* Second pass: build the string */
    d = opendir(path);
    if (!d) return result;
    int pos = 0;
    int first = 1;
    while ((ent = readdir(d)) != NULL) {
        const char* name = ent->d_name;
        if (name[0] == '.' && (name[1] == '\0' || (name[1] == '.' && name[2] == '\0'))) {
            continue;
        }
        if (!first) {
            result[pos++] = '\n';
        }
        int nlen = (int)strlen(name);
        memcpy(result + pos, name, nlen);
        pos += nlen;
        first = 0;
    }
    result[pos] = '\0';
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
 * everywhere for CRLF tolerance. Returns a GC-allocated string. */
static char* io_read_line_stream(FILE* f) {
    if (!f) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    size_t cap = 128, len = 0;
    char* buf = (char*)_emperor_gc_alloc((int)cap, 1);
    if (!buf) return buf;
    int c;
    while ((c = fgetc(f)) != EOF) {
        if (c == '\n') break;
        if (c == '\r') continue;
        if (len + 2 > cap) {
            cap *= 2;
            char* grown = (char*)_emperor_gc_alloc((int)cap, 1);
            if (!grown) break;
            memcpy(grown, buf, len);
            buf = grown;
        }
        buf[len++] = (char)c;
    }
    buf[len] = '\0';
    return buf;
}

/* Whole remaining stream (from the current position) as one GC string. */
static char* io_read_all_stream(FILE* f) {
    size_t cap = 4096, len = 0;
    char* buf = (char*)_emperor_gc_alloc((int)cap, 1);
    if (!buf) return buf;
    for (;;) {
        size_t got = fread(buf + len, 1, cap - len - 1, f);
        len += got;
        if (got == 0) break;
        if (cap - len < 2) {
            cap *= 2;
            char* grown = (char*)_emperor_gc_alloc((int)cap, 1);
            if (!grown) break;
            memcpy(grown, buf, len);
            buf = grown;
        }
    }
    buf[len] = '\0';
    return buf;
}

/* console / stdin */
char* std_io_stdin_read_line(void) {
    return io_read_line_stream(stdin);
}

char std_io_stdin_eof(void) {
    return (char)(feof(stdin) ? 1 : 0);
}

char* std_io_stdin_read_all(void) {
    return io_read_all_stream(stdin);
}

/* File handles: FILE* passed through PenguinLang as i64/u64 (0 = invalid). */
long long std_io_file_open(const char* path, const char* mode) {
    if (!path || !mode || !mode[0]) return 0;
    FILE* f = fopen(path, mode);
    if (!f) return 0;
    return (long long)(intptr_t)f;
}

void std_io_file_close(long long handle) {
    if (handle != 0) {
        fclose((FILE*)(intptr_t)handle);
    }
}

char std_io_file_write(long long handle, const char* s) {
    if (handle == 0 || !s) return 0;
    return fputs(s, (FILE*)(intptr_t)handle) >= 0 ? 1 : 0;
}

char* std_io_file_read_line(long long handle) {
    if (handle == 0) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
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

char* std_io_file_read_all(long long handle) {
    if (handle == 0) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
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
char std_io_file_write_text(const char* path, const char* text) {
    if (!path) return 0;
    FILE* f = fopen(path, "w");
    if (!f) return 0;
    int ok = 1;
    if (text && text[0]) {
        ok = (fputs(text, f) >= 0);
    }
    ok = (fclose(f) == 0) && ok;
    return (char)(ok ? 1 : 0);
}

char std_io_file_append_text(const char* path, const char* text) {
    if (!path) return 0;
    FILE* f = fopen(path, "a");
    if (!f) return 0;
    int ok = 1;
    if (text && text[0]) {
        ok = (fputs(text, f) >= 0);
    }
    ok = (fclose(f) == 0) && ok;
    return (char)(ok ? 1 : 0);
}

char std_io_file_remove(const char* path) {
    if (!path) return 0;
    return remove(path) == 0 ? 1 : 0;
}

char std_io_file_rename(const char* from, const char* to) {
    if (!from || !to) return 0;
    return rename(from, to) == 0 ? 1 : 0;
}

/* Query helpers reused from the legacy (pre-std.io) runtime entry points.
 * The std.io externs above declare these under their mangled names
 * (std.io.file_read_text -> std_io_file_read_text); the legacy
 * _utils externs of the compiler itself still bind the original symbols, so
 * both names must resolve. Thin aliases — single implementation stays put. */
char* std_io_file_read_text(const char* path) {
    return _emperor_file_read_text(path);
}

char std_io_file_exists(const char* path) {
    return _emperor_file_exists(path);
}

char std_io_dir_exists(const char* path) {
    return _emperor_dir_exists(path);
}

long long std_io_file_size(const char* path) {
    return _emperor_file_size(path);
}

char* std_io_dir_get_entries(const char* path) {
    return _emperor_dir_get_entries(path);
}

char std_io_file_mkdir(const char* path) {
    return _emperor_mkdir(path);
}

/* --- StringBuilder --- */

/* Layout must match EmperorPenguin's StringBuilder class: a metadata ptr at
 * offset 0 (every EmperorPenguin object has one), then data/len/cap. The
 * PenguinLang class declares `data: string; len: i32; cap: i32;` so the
 * emitter lays out [metadata, data, len, cap] identically. */
typedef struct StringBuilder {
    void* metadata;
    char* data;
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
    sb->data = (char*)_emperor_gc_alloc(sb->cap, 1);
    sb->len = 0;
    if (sb->data) sb->data[0] = '\0';
}

void _emperor_StringBuilder_append(void* vsb, const char* s) {
    if (!vsb || !s) return;
    StringBuilder* sb = (StringBuilder*)vsb;
    int slen = (int)strlen(s);
    while (sb->len + slen + 1 > sb->cap) {
        sb->cap *= 2;
        char* newdata = (char*)_emperor_gc_alloc(sb->cap, 1);
        if (newdata) {
            memcpy(newdata, sb->data, sb->len);
            newdata[sb->len] = '\0';
        }
        sb->data = newdata;
    }
    if (sb->data) {
        memcpy(sb->data + sb->len, s, slen);
        sb->len += slen;
        sb->data[sb->len] = '\0';
    }
}

// Copy a value-type (ICopy) class instance. The metadata stores the instance
// size at offset 8 (after the name pointer). This is used when the LLVM backend
// emits a call to __builtin_ICopy_copy for value-type copies.
void* _emperor_ICopy_copy(void* this_ptr) {
    void* meta = *(void**)this_ptr;
    int size = *(int*)(meta + 8);
    void* new_obj = _emperor_alloc_impl(size);
    if (new_obj) { memcpy(new_obj, this_ptr, size); }
    return new_obj;
}

char* _emperor_StringBuilder_to_string(void* vsb) {
    if (!vsb) {
        char* r = (char*)_emperor_gc_alloc(1, 1);
        if (r) r[0] = '\0';
        return r;
    }
    StringBuilder* sb = (StringBuilder*)vsb;
    int len = sb->len;
    char* result = (char*)_emperor_gc_alloc(len + 1, 1);
    if (result) {
        if (sb->data) memcpy(result, sb->data, len);
        result[len] = '\0';
    }
    return result;
}
