/* meta_stubs.c — strong stub definitions for the __builtin.penguin_jit_*
 * C API, linked in builds that do NOT carry the real LLVM ORC JIT
 * (libpenguin_jit.a): every Windows cross build, and any Linux build run
 * without -enable-meta.
 *
 * The compiler's own MetaEngine.penguin always references these symbols
 * (src/meta/ is part of the full source set), so without any definition the
 * final link fails with undefined references even though meta is never
 * invoked in such a build. These stubs make the link succeed and report a
 * clear error if a JIT call is ever attempted, mirroring how BabyPenguin's
 * pass-1 VM exposes no-op JIT builtins.
 *
 * They are deliberately STRONG, not weak: a weak stub living in
 * libcore_builtin.a would be pulled before libpenguin_jit.a and (with lld)
 * shadow the real JIT symbols — silently disabling meta in -enable-meta
 * builds. main.penguin therefore links this object ONLY when the real JIT is
 * absent, and the Makefile keeps it OUT of libcore_builtin.a.
 */

#include "penguin_jit.h"
#include <stddef.h>

penguin_jit_ctx_t _emperor_penguin_jit_create(void) {
    return NULL;
}

int _emperor_penguin_jit_add_module(penguin_jit_ctx_t ctx,
                                    const char* name,
                                    const char* ir_text) {
    (void)ctx; (void)name; (void)ir_text;
    return 1;
}

void* _emperor_penguin_jit_lookup(penguin_jit_ctx_t ctx, const char* name) {
    (void)ctx; (void)name;
    return NULL;
}

void _emperor_penguin_jit_destroy(penguin_jit_ctx_t ctx) {
    (void)ctx;
}

const char* _emperor_penguin_jit_get_error(void) {
    return "meta JIT unavailable in this build (no LLVM ORC linked)";
}

int64_t _emperor_penguin_jit_call_i64_0(void* fn) {
    (void)fn;
    return 0;
}

int64_t _emperor_penguin_jit_call_i64_i64(void* fn, int64_t a) {
    (void)fn; (void)a;
    return 0;
}

int64_t _emperor_penguin_jit_call_i64_i64_i64(void* fn, int64_t a, int64_t b) {
    (void)fn; (void)a; (void)b;
    return 0;
}

int64_t _emperor_penguin_jit_call_i64_i64_i64_i64(void* fn, int64_t a,
                                                 int64_t b, int64_t c) {
    (void)fn; (void)a; (void)b; (void)c;
    return 0;
}

void* _emperor_penguin_jit_call_ptr_ptr(void* fn, void* a) {
    (void)fn; (void)a;
    return NULL;
}

void* _emperor_penguin_jit_call_ptr_ptr_ptr(void* fn, void* a, void* b) {
    (void)fn; (void)a; (void)b;
    return NULL;
}
