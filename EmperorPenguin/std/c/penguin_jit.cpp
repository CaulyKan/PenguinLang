/* penguin_jit.cpp — LLVM ORC JIT wrapper exposing a pure C API.
 *
 * Thin wrapper around llvm::orc::LLJIT for the EmperorPenguin meta-execution
 * engine. Uses LLVM 22 APIs (Expected<ExecutorAddr>, DynamicLibrarySearchGenerator
 * in ExecutionUtils.h).
 *
 * All errors are reported via a thread-local string; no exceptions are used
 * (consistent with -fno-exceptions).
 */

#include "penguin_jit.h"

#include <llvm/ExecutionEngine/Orc/LLJIT.h>
#include <llvm/ExecutionEngine/Orc/Core.h>
#include <llvm/ExecutionEngine/Orc/ExecutionUtils.h>
#include <llvm/IRReader/IRReader.h>
#include <llvm/Support/SourceMgr.h>
#include <llvm/Support/TargetSelect.h>
#include <llvm/ExecutionEngine/Orc/ThreadSafeModule.h>

#include <memory>
#include <string>

using namespace llvm;
using namespace llvm::orc;

/* ------------------------------------------------------------------ */
/* Internal state                                                      */
/* ------------------------------------------------------------------ */

struct penguin_jit_ctx_s {
  std::unique_ptr<LLJIT> jit;
};

/* Thread-local buffer for the last error message. */
static thread_local std::string last_error;

/* One-shot LLVM native target initialisation. */
static bool init_llvm_once() {
  static bool initialised = false;
  static bool success = false;
  if (!initialised) {
    initialised = true;
    InitializeNativeTarget();
    InitializeNativeTargetAsmPrinter();
    InitializeNativeTargetAsmParser();
    success = true;
  }
  return success;
}

/* ------------------------------------------------------------------ */
/* Public API                                                          */
/* ------------------------------------------------------------------ */

penguin_jit_ctx_t _emperor_penguin_jit_create(void) {
  init_llvm_once();

  auto J = LLJITBuilder().create();
  if (!J) {
    last_error = "LLJITBuilder::create: " + toString(J.takeError());
    return nullptr;
  }

  auto ctx = std::make_unique<penguin_jit_ctx_s>();
  ctx->jit = std::move(*J);

  /* Attach host-process symbol resolution so JIT'd code can call host
   * functions (e.g. _emperor_println, _emperor_alloc_impl, host_test_fn). */
  JITDylib &JD = ctx->jit->getMainJITDylib();
  const DataLayout &DL = ctx->jit->getDataLayout();
  char GlobalPrefix = DL.getGlobalPrefix();

  auto G = DynamicLibrarySearchGenerator::GetForCurrentProcess(GlobalPrefix);
  if (!G) {
    last_error = "DynamicLibrarySearchGenerator::GetForCurrentProcess: "
               + toString(G.takeError());
    /* Return the context anyway — host resolution will simply be missing,
     * and the caller can still JIT-compile self-contained modules. */
  } else {
    JD.addGenerator(std::move(*G));
  }

  return ctx.release();
}

int _emperor_penguin_jit_add_module(penguin_jit_ctx_t ctx,
                           const char *name,
                           const char *ir_text) {
  if (!ctx) {
    last_error = "penguin_jit_add_module: null context";
    return 1;
  }

  /* Each module gets its own LLVMContext for thread safety. */
  auto Ctx = std::make_unique<LLVMContext>();

  SMDiagnostic Err;
  auto M = parseIR(*MemoryBuffer::getMemBuffer(ir_text, name), Err, *Ctx);
  if (!M) {
    last_error = "parseIR: " + Err.getMessage().str();
    return 1;
  }

  /* Each #fun is compiled as its own unit-B module that re-emits the whole
   * stdlib (Option::is_some, string helpers, ...). Adding a second such module
   * to the same JITDylib would otherwise error with "duplicate definition".
   * Mark every definition weak so the JIT keeps the first definition and
   * ignores the re-definitions; the (uniquely-named) #fun itself is unaffected. */
  for (auto &F : *M) {
    if (!F.isDeclaration()) {
      F.setLinkage(GlobalValue::WeakAnyLinkage);
    }
  }
  for (auto &G : M->globals()) {
    if (!G.isDeclaration()) {
      G.setLinkage(GlobalValue::WeakAnyLinkage);
    }
  }

  ThreadSafeModule TSM(std::move(M), std::move(Ctx));

  auto E = ctx->jit->addIRModule(std::move(TSM));
  if (E) {
    last_error = "addIRModule: " + toString(std::move(E));
    return 1;
  }

  return 0;
}

void *_emperor_penguin_jit_lookup(penguin_jit_ctx_t ctx, const char *name) {
  if (!ctx) {
    last_error = "penguin_jit_lookup: null context";
    return nullptr;
  }

  auto S = ctx->jit->lookup(name);
  if (!S) {
    last_error = "lookup: " + toString(S.takeError());
    return nullptr;
  }

  /* LLVM 22: lookup() returns Expected<ExecutorAddr>.
   * ExecutorAddr::getValue() returns uint64_t. */
  return reinterpret_cast<void *>(
      static_cast<uintptr_t>(S->getValue()));
}

void _emperor_penguin_jit_destroy(penguin_jit_ctx_t ctx) {
  delete ctx;
}

const char *_emperor_penguin_jit_get_error(void) {
  return last_error.c_str();
}

/* ------------------------------------------------------------------ */
/* Trampolines                                                         */
/* ------------------------------------------------------------------ */

extern "C" int64_t _emperor_penguin_jit_call_i64_0(void *fn) {
  return reinterpret_cast<int64_t (*)()>(fn)();
}

extern "C" int64_t _emperor_penguin_jit_call_i64_i64(void *fn, int64_t a) {
  return reinterpret_cast<int64_t (*)(int64_t)>(fn)(a);
}

extern "C" int64_t _emperor_penguin_jit_call_i64_i64_i64(void *fn,
                                                int64_t a,
                                                int64_t b) {
  return reinterpret_cast<int64_t (*)(int64_t, int64_t)>(fn)(a, b);
}

extern "C" int64_t _emperor_penguin_jit_call_i64_i64_i64_i64(void *fn,
                                                    int64_t a,
                                                    int64_t b,
                                                    int64_t c) {
  return reinterpret_cast<int64_t (*)(int64_t, int64_t, int64_t)>(fn)(a, b, c);
}

extern "C" void *_emperor_penguin_jit_call_ptr_ptr(void *fn, void *a) {
  return reinterpret_cast<void *(*)(void *)>(fn)(a);
}

extern "C" void *_emperor_penguin_jit_call_ptr_ptr_ptr(void *fn, void *a, void *b) {
  return reinterpret_cast<void *(*)(void *, void *)>(fn)(a, b);
}
