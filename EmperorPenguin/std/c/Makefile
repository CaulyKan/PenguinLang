# Cross-compilation support:
#   CROSS=win64   → use /opt/llvm-mingw x86_64-w64-mingw32 toolchain
#   OPT=-O2       → optimization level (appended to CFLAGS)
#   CC / AR / CFLAGS can also be set directly from the environment.

ifeq ($(CROSS),win64)
  CC  ?= /opt/llvm-mingw/bin/x86_64-w64-mingw32-clang
  AR  ?= /opt/llvm-mingw/bin/x86_64-w64-mingw32-ar
else
  CC  ?= clang
  AR  ?= llvm-ar
endif

CFLAGS ?= -I../include -g
CFLAGS += $(OPT)

CXX ?= clang++
CXXFLAGS ?= -I../include -g -std=c++17
LLVM_CONFIG ?= llvm-config
LLVM_CXXFLAGS := $(shell $(LLVM_CONFIG) --cxxflags 2>/dev/null)
LLVM_LDFLAGS  := $(shell $(LLVM_CONFIG) --ldflags --libs core orcjit native 2>/dev/null)

SRC = core_builtin.c gc.c penguinlang_interop.c
OBJ = $(SRC:.c=.o)

OUTPUT_DIR ?= .
LIB = $(OUTPUT_DIR)/libcore_builtin.a

# Only build libpenguin_jit.a when LLVM is detected
ifneq ($(strip $(LLVM_CXXFLAGS)),)
  JIT_LIB = $(OUTPUT_DIR)/libpenguin_jit.a
endif

all: $(LIB) $(JIT_LIB)

$(LIB): $(OBJ)
	$(AR) rcs $@ $^

$(JIT_LIB): penguin_jit.cpp penguin_jit.h
	$(CXX) $(CXXFLAGS) $(LLVM_CXXFLAGS) -c penguin_jit.cpp -o penguin_jit.o
	$(AR) rcs $@ penguin_jit.o

%.o: %.c
	$(CC) $(CFLAGS) -c -o $@ $<

clean:
	rm -f $(OBJ) $(LIB) penguin_jit.o $(JIT_LIB)

.PHONY: all clean
