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

SRC = core_builtin.c gc.c penguinlang_interop.c
OBJ = $(SRC:.c=.o)

OUTPUT_DIR ?= .
LIB = $(OUTPUT_DIR)/libcore_builtin.a

all: $(LIB)

$(LIB): $(OBJ)
	$(AR) rcs $@ $^

%.o: %.c
	$(CC) $(CFLAGS) -c -o $@ $<

clean:
	rm -f $(OBJ) $(LIB)

.PHONY: all clean
