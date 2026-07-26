#ifndef PENGUIN_JIT_H
#define PENGUIN_JIT_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Opaque handle to an LLVM ORC JIT session. */
typedef struct penguin_jit_ctx_s* penguin_jit_ctx_t;

/* Create a new JIT session. Returns NULL on failure (call penguin_jit_get_error
 * for details). The returned context must be destroyed with penguin_jit_destroy. */
penguin_jit_ctx_t penguin_jit_create(void);

/* Add an LLVM IR module to the JIT session. `name` is a descriptive label
 * used for diagnostics. `ir_text` must be well-formed LLVM IR in textual form.
 * Returns 0 on success, non-zero on failure. */
int penguin_jit_add_module(penguin_jit_ctx_t ctx, const char* name, const char* ir_text);

/* Look up a symbol by name in the JIT session. Returns the function/global
 * pointer, or NULL on failure. */
void* penguin_jit_lookup(penguin_jit_ctx_t ctx, const char* name);

/* Destroy a JIT session and release all resources. The context pointer is
 * invalid after this call. */
void penguin_jit_destroy(penguin_jit_ctx_t ctx);

/* Return the last error message (thread-local). The returned string is valid
 * until the next call to any penguin_jit_* function on the same thread. */
const char* penguin_jit_get_error(void);

/* --- Trampolines for calling JIT-compiled functions with various signatures ---
 *
 * Each trampoline takes a raw function pointer (void*) as the first argument
 * followed by the call arguments, casts fn to the correct function-pointer
 * type, and invokes it. These are necessary because the PenguinLang side
 * cannot express arbitrary function-pointer types.
 */
int64_t penguin_jit_call_i64_0(void* fn);
int64_t penguin_jit_call_i64_i64(void* fn, int64_t a);
int64_t penguin_jit_call_i64_i64_i64(void* fn, int64_t a, int64_t b);
int64_t penguin_jit_call_i64_i64_i64_i64(void* fn, int64_t a, int64_t b, int64_t c);
void*   penguin_jit_call_ptr_ptr(void* fn, void* a);
void*   penguin_jit_call_ptr_ptr_ptr(void* fn, void* a, void* b);

#ifdef __cplusplus
}
#endif

#endif /* PENGUIN_JIT_H */
