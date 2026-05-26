#include "emperor_builtin.h"
#include "emperor_gc.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/types.h>

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

/* Called from main() to store argc/argv */
void _emperor_args_init(int argc, char** argv) {
    g_argc = argc;
    g_argv = argv;
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

/* --- StringBuilder --- */

typedef struct StringBuilder {
    char* data;
    int len;
    int cap;
} StringBuilder;

void* _emperor_stringbuilder_new(void) {
    StringBuilder* sb = (StringBuilder*)_emperor_gc_alloc(sizeof(StringBuilder), 0);
    if (sb) {
        sb->cap = 256;
        sb->data = (char*)_emperor_gc_alloc(sb->cap, 1);
        sb->len = 0;
        if (sb->data) sb->data[0] = '\0';
    }
    return sb;
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
