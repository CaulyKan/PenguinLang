CC = cc
CFLAGS = -I../include
AR = ar

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
