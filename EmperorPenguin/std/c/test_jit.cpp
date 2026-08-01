/* test_jit.cpp — Smoke test for libpenguin_jit (LLVM ORC JIT C wrapper).
 *
 * Verifies:
 *   1. JIT context creation
 *   2. IR module addition (+ parseIR)
 *   3. Symbol lookup and function call
 *   4. Host-symbol resolution via DynamicLibrarySearchGenerator
 */

#include "penguin_jit.h"

#include <cstdio>

/* A host function that the JIT'd code will resolve at runtime. */
extern "C" int host_test_fn(int x) {
  return x + 100;
}

int main() {
  /* 1. Create JIT context */
  auto ctx = _emperor_penguin_jit_create();
  if (!ctx) {
    std::printf("FAIL create: %s\n", _emperor_penguin_jit_get_error());
    return 1;
  }

  /* 2. Add an IR module with two functions:
   *      test_fn   — self-contained, returns 42
   *      test_host — calls host_test_fn(5), expects 105 */
  const char *ir =
    "define i32 @test_fn() { ret i32 42 }\n"
    "declare i32 @host_test_fn(i32)\n"
    "define i32 @test_host() { %r = call i32 @host_test_fn(i32 5)\n"
    "  ret i32 %r }\n";

  if (_emperor_penguin_jit_add_module(ctx, "test", ir) != 0) {
    std::printf("FAIL add_module: %s\n", _emperor_penguin_jit_get_error());
    return 1;
  }

  /* 3. Look up and call test_fn — expect 42 */
  using int_fn = int (*)();
  auto fn = reinterpret_cast<int_fn>(_emperor_penguin_jit_lookup(ctx, "test_fn"));
  if (!fn) {
    std::printf("FAIL lookup test_fn: %s\n", _emperor_penguin_jit_get_error());
    return 1;
  }
  int r1 = fn();
  if (r1 != 42) {
    std::printf("FAIL jit-call got %d, expected 42\n", r1);
    return 1;
  }

  /* 4. Look up and call test_host — expect 105 */
  auto fh = reinterpret_cast<int_fn>(_emperor_penguin_jit_lookup(ctx, "test_host"));
  if (!fh) {
    std::printf("FAIL lookup test_host: %s\n", _emperor_penguin_jit_get_error());
    return 1;
  }
  int r2 = fh();
  if (r2 != 105) {
    std::printf("FAIL host-resolve got %d, expected 105\n", r2);
    return 1;
  }

  /* 5. Cleanup */
  _emperor_penguin_jit_destroy(ctx);
  std::printf("PASS jit-smoke\n");
  return 0;
}
