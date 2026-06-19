#include "emperor_builtin.h"
#include "emperor_gc.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/types.h>
#ifdef _WIN32
#include <windows.h>
#else
#include <dirent.h>
#endif

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
    exit(code);
}

/* --- Allocation --- */

void* _emperor_alloc_impl(int size) {
    return _emperor_gc_alloc(size, 0);
}

/* --- Conversions --- */

char* _emperor_int_to_string(int value) {
    char* buf = (char*)_emperor_gc_alloc(32, 1);
    if (buf) {
        snprintf(buf, 32, "%d", value);
    }
    return buf;
}

char* _emperor_i64_to_string(long long value) {
    char* buf = (char*)_emperor_gc_alloc(32, 1);
    if (buf) {
        snprintf(buf, 32, "%lld", value);
    }
    return buf;
}

char* _emperor_string_concat(const char* a, const char* b) {
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
 * calls this as `call void @_emperor_stringbuilder_new(ptr %this)` — it does
 * NOT use a return value (the PenguinLang `new` is `mut this`, void return) —
 * so we must fill in the fields of the passed-in object, not allocate a new
 * one (the old `void* ...(void)` factory form was ignored by the caller and
 * left `this` uninitialized). */
void _emperor_stringbuilder_new(void* vsb) {
    if (!vsb) return;
    StringBuilder* sb = (StringBuilder*)vsb;
    sb->cap = 256;
    sb->data = (char*)_emperor_gc_alloc(sb->cap, 1);
    sb->len = 0;
    if (sb->data) sb->data[0] = '\0';
}

void _emperor_stringbuilder_append(void* vsb, const char* s) {
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

char* _emperor_stringbuilder_to_string(void* vsb) {
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
