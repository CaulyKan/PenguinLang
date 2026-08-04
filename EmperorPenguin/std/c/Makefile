# Cross-compilation support:
#   CROSS=win64   → use /opt/llvm-mingw x86_64-w64-mingw32 toolchain
#   OPT=-O2       → optimization level (appended to CFLAGS)
#   LLVM_WIN_PREFIX=<dir> → dir containing a WINDOWS LLVM for the cross JIT:
#       <dir>/include   (LLVM 22 headers, llvm-config.h with LLVM_ON_WIN32)
#       <dir>/lib/libLLVM-22.dll.a  (import lib generated from libLLVM-22.dll)
#   Defaults to the in-repo thirdparty/mingw-w64-x86_64-llvm-libs package; only
#   used when it actually contains the import lib (LLVM_WIN_OK non-empty).
#   When present AND cross-compiling, libpenguin_jit.a is built for Windows so
#   `-enable-meta` works in Windows builds (meta JIT links the DLL at runtime).
#   CC / AR / CFLAGS / CXX can also be set directly from the environment.

ifeq ($(CROSS),win64)
  CC  ?= /opt/llvm-mingw/bin/x86_64-w64-mingw32-clang
  CXX ?= /opt/llvm-mingw/bin/x86_64-w64-mingw32-clang++
  AR  ?= /opt/llvm-mingw/bin/x86_64-w64-mingw32-ar
else
  CC  ?= clang
  CXX ?= clang++
  AR  ?= llvm-ar
endif

CFLAGS ?= -I../include -g
CFLAGS += $(OPT)

CXXFLAGS ?= -I../include -g -std=c++17
LLVM_CONFIG ?= llvm-config
LLVM_CXXFLAGS := $(shell $(LLVM_CONFIG) --cxxflags 2>/dev/null)
LLVM_LDFLAGS  := $(shell $(LLVM_CONFIG) --ldflags --libs core orcjit native 2>/dev/null)

# In-repo Windows LLVM for the cross JIT (see thirdparty/mingw-w64-x86_64-llvm-libs/README.md).
# LLVM_WIN_OK is the prefix only when the import lib is actually present, so a
# fresh clone without the package silently builds non-meta Windows instead of failing.
LLVM_WIN_PREFIX ?= $(abspath ../../../thirdparty/mingw-w64-x86_64-llvm-libs)
LLVM_WIN_OK := $(strip $(if $(wildcard $(LLVM_WIN_PREFIX)/lib/libLLVM-22.dll.a),$(LLVM_WIN_PREFIX),))

SRC = core_builtin.c gc.c penguinlang_interop.c

OUTPUT_DIR ?= .
# Compile every object into $(OUTPUT_DIR), never the shared source directory:
# each compiler invocation runs `make -C <src> OUTPUT_DIR=<its own temp dir>`
# (EmperorPenguin/main.penguin), and parallel test compiles would otherwise race
# on the shared *.o files in the source dir (clang++'s unlink-then-write window
# makes a concurrent `ar` fail with "no such file"). With per-invocation OUTPUT_DIR
# the objects are private to that compile, so concurrent makes cannot collide.
OBJ = $(addprefix $(OUTPUT_DIR)/, $(SRC:.c=.o))
LIB = $(OUTPUT_DIR)/libcore_builtin.a

# Strong no-op stubs for the __builtin.penguin_jit_* C API (see meta_stubs.c).
# Built for every compile but kept OUT of libcore_builtin.a: main.penguin links
# meta_stubs.o directly when the real libpenguin_jit.a is absent (Windows cross
# builds, and Linux builds without -enable-meta). Keeping it out of the archive
# prevents it from shadowing the real JIT symbols in -enable-meta builds.
STUB_OBJ = $(OUTPUT_DIR)/meta_stubs.o

# Only build libpenguin_jit.a when there is an LLVM to compile it against:
#   - host build:  when the host LLVM is detected (llvm-config --cxxflags);
#   - cross build: when a Windows LLVM is available (LLVM_WIN_OK non-empty).
# penguin_jit.cpp pulls in the LLVM ORC headers; a cross build cannot use the
# host LLVM (mingw clang can't consume Linux LLVM headers cleanly), so without
# a Windows LLVM the JIT is skipped for cross builds (meta/JIT is then a
# Linux-only feature for that binary). `IS_CROSS` is set when `CROSS=win64` is
# passed explicitly or when CC names a mingw cross compiler (as `./penguin -p`
# does for the win64 publish).
IS_CROSS := $(strip $(or $(CROSS),$(findstring mingw,$(CC))))
ifneq ($(IS_CROSS),)
  ifneq ($(LLVM_WIN_OK),)
    JIT_LIB = $(OUTPUT_DIR)/libpenguin_jit.a
  endif
else
  ifneq ($(strip $(LLVM_CXXFLAGS)),)
    JIT_LIB = $(OUTPUT_DIR)/libpenguin_jit.a
  endif
endif

# Flags for compiling penguin_jit.cpp: the host (Linux) build ALWAYS uses the
# host LLVM headers via llvm-config — the Windows headers under LLVM_WIN_OK are
# only for the cross build. (LLVM_WIN_OK is non-empty on the host too once the
# in-repo thirdparty package exists, so gating on it alone would make host
# builds consume the Windows headers and fail.)
ifeq ($(IS_CROSS),)
  JIT_CXXFLAGS = $(LLVM_CXXFLAGS)
else
  JIT_CXXFLAGS = -I$(LLVM_WIN_OK)/include
endif

all: $(LIB) $(JIT_LIB) $(STUB_OBJ)

$(LIB): $(OBJ)
	$(AR) rcs $@ $^

$(OUTPUT_DIR)/penguin_jit.o: penguin_jit.cpp penguin_jit.h
	$(CXX) $(CXXFLAGS) $(JIT_CXXFLAGS) -c penguin_jit.cpp -o $@

$(JIT_LIB): $(OUTPUT_DIR)/penguin_jit.o
	$(AR) rcs $@ $^

$(OUTPUT_DIR)/%.o: %.c
	$(CC) $(CFLAGS) -c -o $@ $<

clean:
	rm -f $(OBJ) $(LIB) $(STUB_OBJ) $(OUTPUT_DIR)/penguin_jit.o $(JIT_LIB)

.PHONY: all clean
