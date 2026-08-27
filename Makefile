# PenguinLang root Makefile
# =====================================================================
# Replaces the old ./penguin shell script. All build artifacts live under
# build/ (gitignored).
#
#   make clean           remove the build/ tree
#   make bootstrap       self-bootstrap EmperorPenguin (see layout below)
#   make lsp             build the PenguinLang-native LSP server
#   make test            run the cross-compiler markdown suite (Tests/*.md)
#   make baseline_test   like `test`, but record the run as the new baseline
#   make publish         release: native compilers + LSP servers into the
#                        vscode extension, dotnet self-contained publishes,
#                        vsix package
#   make all             bootstrap + lsp + test, in that order (default)
#
# Variables (overridable via environment or make command line):
#   TARGET=win|linux     compile target (default: the host; `make publish`
#                        with no TARGET builds BOTH on a linux host).
#                        Cross compiling is linux->win only (llvm-mingw);
#                        a windows host builds natively (TARGET=win).
#                        `bootstrap` always targets the host — the
#                        bootstrapped compiler is itself the build tool.
#   TEST_ARGS="..."      extra args for `make test` / `make baseline_test`
#                        (e.g. TEST_ARGS="--filter LspTest/*")
#   LSP_NO_CACHE=1       skip the LSP content-addressed caches
#   WINE=<wine binary>   wine used for the windows publish smoke test on a
#                        linux host (on a windows host the smoke runs natively)
#   MINGW_PREFIX, WIN_CC, WIN_CXX, WIN_AR, WIN_CLANG
#                        llvm-mingw cross toolchain (linux -> win)
#   LLVM_WIN_PREFIX      dir with a Windows LLVM for the meta JIT (needs
#                        lib/libLLVM-22.dll.a + include/); defaults to
#                        thirdparty/mingw-w64-x86_64-llvm-libs when present.
#                        REQUIRED for `bootstrap`/`publish` on a windows host
#                        (every bootstrap stage is built with -enable-meta).
#
# Bootstrap layout (all under build/):
#   pass1  BabyPenguin (dotnet, --backend=cs) lowers EmperorPenguin to C#,
#          runs it, and it emits LLVM IR + clang -> build/pass2
#   pass2  build/pass2 (Full monolith) -> build/pass3
#   linux: pass3/pass4/pass5 are lib+exe pairs (EmperorPenguinLib/Exe
#          projects) — pass5 exists only for the md5 convergence check
#          (exe AND lib; the exe alone only contains the CLI driver);
#          build/pass4 is a symlink into build/pass4.d
#   win:   the .penguin-lib pair is ELF-specific (SONAME/$ORIGIN/rpath/
#          -rdynamic), so pass3/pass4/pass5 stay Full monoliths and the
#          convergence check compares the exe md5s only
# =====================================================================

ifeq ($(OS),Windows_NT)
  HOST := win
else
  HOST := linux
endif

# TARGET is empty when the user did not ask for a specific one; remember
# that so `publish` can default to "both" on a linux host.
ifeq ($(strip $(TARGET)),)
  TARGET := $(HOST)
  TARGET_DEFAULTED := 1
endif

ifeq ($(HOST),win)
  SUPPORTED_TARGETS := win
else
  SUPPORTED_TARGETS := linux win
endif
ifeq ($(filter $(TARGET),$(SUPPORTED_TARGETS)),)
  $(error unsupported TARGET='$(TARGET)' on a $(HOST) host (supported: $(SUPPORTED_TARGETS)))
endif

.DEFAULT_GOAL := all
.PHONY: all clean bootstrap lsp test baseline_test publish

# Prefer bash for `set -o pipefail` (pipeline exit codes; see the tee'd
# stage logs below — without pipefail a failing stage upstream of tee
# reports tee's 0 and the bootstrap falsely succeeds).
SHELL := $(shell command -v bash 2>/dev/null || echo /bin/sh)

# Optional `ts` (moreutils) timestamps the piped logs; plain tee otherwise.
TS := $(shell command -v ts 2>/dev/null)
ifneq ($(strip $(TS)),)
  TEE = 2>&1 | ts "%F %T" | tee
else
  TEE = 2>&1 | tee
endif

# ── Cross toolchain (linux -> win) ───────────────────────────────────
MINGW_PREFIX ?= /opt/llvm-mingw
WIN_CC    ?= $(MINGW_PREFIX)/bin/x86_64-w64-mingw32-clang
WIN_CXX   ?= $(MINGW_PREFIX)/bin/x86_64-w64-mingw32-clang++
WIN_AR    ?= $(MINGW_PREFIX)/bin/x86_64-w64-mingw32-ar
WIN_CLANG ?= $(WIN_CC)

# Applied per-invocation (NOT exported globally) so `make all TARGET=win`
# cannot leak the mingw tools into the host-native bootstrap sub-make.
ifeq ($(HOST)$(TARGET),linuxwin)
  XENV := CC=$(WIN_CC) CXX=$(WIN_CXX) AR=$(WIN_AR) CLANG=$(WIN_CLANG)
endif

# ── Windows LLVM for the meta JIT ────────────────────────────────────
# Auto-detected from the vendored package (win32 cross from linux, and the
# native-windows flow — the compiler's windows -enable-meta link path needs
# exactly this layout: <prefix>/lib/libLLVM-22.dll.a + <prefix>/include).
LLVM_WIN_PREFIX ?= $(if $(wildcard thirdparty/mingw-w64-x86_64-llvm-libs/lib/libLLVM-22.dll.a),$(abspath thirdparty/mingw-w64-x86_64-llvm-libs),)
ifneq ($(LLVM_WIN_PREFIX),)
  WIN_META := -enable-meta -llvm-win $(LLVM_WIN_PREFIX)
endif

# ── Native windows host ──────────────────────────────────────────────
# MSYS2-style environment (make/clang/llvm-ar on PATH). Every compiler
# invocation needs -target=win64 (PE stack flags, two-argument _setjmp —
# the mingw sjlj ABI), and the C runtime makefile is steered onto the
# Windows-JIT path (thirdparty headers) instead of probing a host
# llvm-config, by exporting CROSS=win64 with explicit tool names.
ifeq ($(HOST),win)
  export CC := clang
  export CXX := clang++
  export AR := llvm-ar
  export CLANG := clang
  export CROSS := win64
  ifneq ($(LLVM_WIN_PREFIX),)
    # Every -enable-meta stage loads libLLVM-22.dll at process start; the
    # vendored DLLs live in <pkg>/bin (their own CRT deps come from the
    # MSYS2 toolchain bin dirs already on PATH).
    export PATH := $(LLVM_WIN_PREFIX)/bin:$(PATH)
  else
    GOALS := $(if $(MAKECMDGOALS),$(MAKECMDGOALS),all)
    ifneq ($(filter all bootstrap lsp publish,$(GOALS)),)
      $(error native windows bootstrap/lsp/publish needs a Windows LLVM for -enable-meta: vendor thirdparty/mingw-w64-x86_64-llvm-libs (see its README) or set LLVM_WIN_PREFIX=<dir with lib/libLLVM-22.dll.a>)
    endif
  endif
endif

# ── all ──────────────────────────────────────────────────────────────
# Recursive sub-makes give strict bootstrap -> lsp -> test ordering without
# declaring false dependencies (lsp does not depend on bootstrap — that
# would force a full re-bootstrap on every `make lsp`).
all:
	@$(MAKE) bootstrap
	@$(MAKE) lsp
	@$(MAKE) test

# ── clean ────────────────────────────────────────────────────────────
clean:
	@echo "Cleaning build artifacts ..."
	rm -rf build

# ── bootstrap ────────────────────────────────────────────────────────
bootstrap:
ifeq ($(HOST),linux)
	@echo "Bootstrapping EmperorPenguin (BabyPenguin --backend=cs) ..."
	@echo "============================================================"
	@mkdir -p build
	@set -o pipefail; { \
	dotnet run --configuration Release --project BabyPenguin -- \
	    --backend=cs EmperorPenguin/EmperorPenguinPass1.penguins -- \
	    EmperorPenguin/EmperorPenguinPass1.penguins EmperorPenguin/src/utils.penguin \
	    --disable-dl -vv -enable-meta -o build/pass2 $(TEE) build/pass1.log; \
	} || { echo "Bootstrap FAILED at pass1 (BabyPenguin cs backend -> build/pass2)" >&2; exit 1; }
	@# Pass2 (native) builds pass3 from EmperorPenguinPass2.penguins (the Full
	@# project): the json-backed Dynlib + json/vector/hashmap + _utils are part
	@# of the compiler, making pass3 the first dyn-lib-capable compiler
	@# (--enable-dl default on). pass2 itself is a Full monolith (pass1 built
	@# it from the ANTLR-safe stub project, no dynlib), so the lib+exe split
	@# can only start FROM pass3. --enable-coroutine is passed ONLY here:
	@# pass3 is the first coroutine-capable compiler (ports/channels/scheduler
	@# syntax accepted). Wait-free programs (the compiler itself included)
	@# emit identical binaries with or without the flag.
	@echo "Running Pass 2 with bootstrapping ..."
	@set -o pipefail; { \
	build/pass2 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv -enable-meta --enable-coroutine -o build/pass3 $(TEE) build/pass2.log; \
	} || { echo "Bootstrap FAILED at pass2 (build/pass2 -> build/pass3)" >&2; exit 1; }
	@# Pass4+ are split lib+exe (EmperorPenguinLib/EmperorPenguinExe projects):
	@# the whole compiler is a libemperorpenguin.penguin-lib and the CLI driver
	@# links it, making dyn-lib load-bearing at compiler scale — every
	@# bootstrap exercises the .penguin-lib build/consume path on the
	@# compiler's own 16k lines. The exe finds its lib via rpath $$ORIGIN
	@# (SONAME = lib basename); build/pass4 is a symlink into pass4.d so the
	@# conventional path keeps working from any cwd. pass5 repeats the stage
	@# for the convergence check (exe AND lib md5 equality) and is removed
	@# afterwards.
	@echo "Running Pass 3 (lib) with bootstrapping ..."
	@rm -rf build/pass4.d build/pass5.d
	@mkdir -p build/pass4.d
	@set -o pipefail; { \
	build/pass3 EmperorPenguin/EmperorPenguinLib.penguins \
	    -vv -enable-meta -o build/pass4.d/libemperorpenguin.penguin-lib $(TEE) build/pass3-lib.log; \
	} || { echo "Bootstrap FAILED at pass3 lib (build/pass3 -> build/pass4.d/libemperorpenguin.penguin-lib)" >&2; exit 1; }
	@echo "Running Pass 3 with bootstrapping ..."
	@set -o pipefail; { \
	build/pass3 EmperorPenguin/EmperorPenguinExe.penguins \
	    -vv -enable-meta --lib build/pass4.d/libemperorpenguin.penguin-lib \
	    -o build/pass4.d/pass4 $(TEE) build/pass3.log; \
	} || { echo "Bootstrap FAILED at pass3 (build/pass3 -> build/pass4.d/pass4)" >&2; exit 1; }
	@ln -sf pass4.d/pass4 build/pass4
	@echo "Running Pass 4 (lib) with bootstrapping ..."
	@mkdir -p build/pass5.d
	@set -o pipefail; { \
	build/pass4 EmperorPenguin/EmperorPenguinLib.penguins \
	    -vv -enable-meta -o build/pass5.d/libemperorpenguin.penguin-lib $(TEE) build/pass4-lib.log; \
	} || { echo "Bootstrap FAILED at pass4 lib (build/pass4 -> build/pass5.d/libemperorpenguin.penguin-lib)" >&2; exit 1; }
	@echo "Running Pass 4 with bootstrapping ..."
	@set -o pipefail; { \
	build/pass4 EmperorPenguin/EmperorPenguinExe.penguins \
	    -vv -enable-meta --lib build/pass5.d/libemperorpenguin.penguin-lib \
	    -o build/pass5.d/pass5 $(TEE) build/pass4.log; \
	} || { echo "Bootstrap FAILED at pass4 (build/pass4 -> build/pass5.d/pass5)" >&2; exit 1; }
	@set -e; \
	if command -v md5sum >/dev/null 2>&1; then \
	    HASHEXE4=$$(md5sum build/pass4.d/pass4 | cut -d' ' -f1); \
	    HASHEXE5=$$(md5sum build/pass5.d/pass5 | cut -d' ' -f1); \
	    HASHLIB4=$$(md5sum build/pass4.d/libemperorpenguin.penguin-lib | cut -d' ' -f1); \
	    HASHLIB5=$$(md5sum build/pass5.d/libemperorpenguin.penguin-lib | cut -d' ' -f1); \
	else \
	    HASHEXE4=$$(md5 -q build/pass4.d/pass4); \
	    HASHEXE5=$$(md5 -q build/pass5.d/pass5); \
	    HASHLIB4=$$(md5 -q build/pass4.d/libemperorpenguin.penguin-lib); \
	    HASHLIB5=$$(md5 -q build/pass5.d/libemperorpenguin.penguin-lib); \
	fi; \
	if [ "$$HASHEXE4" = "$$HASHEXE5" ] && [ "$$HASHLIB4" = "$$HASHLIB5" ]; then \
	    rm -rf build/pass5.d; \
	    echo "Bootstrap complete (exe $$HASHEXE4, lib $$HASHLIB4)"; \
	    echo "============================================================"; \
	else \
	    echo "Bootstrap NOT converged: pass4/pass5 differ (exe: $$HASHEXE4 vs $$HASHEXE5, lib: $$HASHLIB4 vs $$HASHLIB5)." >&2; \
	    exit 1; \
	fi
else
	@# ── native windows: Full-monolith chain (no .penguin-lib — ELF-only) ──
	@echo "Bootstrapping EmperorPenguin on Windows (BabyPenguin --backend=cs) ..."
	@echo "============================================================"
	@mkdir -p build
	@set -o pipefail; { \
	dotnet run --configuration Release --project BabyPenguin -- \
	    --backend=cs EmperorPenguin/EmperorPenguinPass1.penguins -- \
	    EmperorPenguin/EmperorPenguinPass1.penguins EmperorPenguin/src/utils.penguin \
	    --disable-dl -vv $(WIN_META) -target=win64 -o build/pass2 $(TEE) build/pass1.log; \
	} || { echo "Bootstrap FAILED at pass1 (BabyPenguin cs backend -> build/pass2)" >&2; exit 1; }
	@echo "Running Pass 2 with bootstrapping ..."
	@set -o pipefail; { \
	build/pass2 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv $(WIN_META) --enable-coroutine -target=win64 -o build/pass3 $(TEE) build/pass2.log; \
	} || { echo "Bootstrap FAILED at pass2 (build/pass2 -> build/pass3)" >&2; exit 1; }
	@echo "Running Pass 3 with bootstrapping ..."
	@set -o pipefail; { \
	build/pass3 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv $(WIN_META) -target=win64 -o build/pass4 $(TEE) build/pass3.log; \
	} || { echo "Bootstrap FAILED at pass3 (build/pass3 -> build/pass4)" >&2; exit 1; }
	@echo "Running Pass 4 with bootstrapping (convergence check) ..."
	@set -o pipefail; { \
	build/pass4 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv $(WIN_META) -target=win64 -o build/pass5 $(TEE) build/pass4.log; \
	} || { echo "Bootstrap FAILED at pass4 (build/pass4 -> build/pass5)" >&2; exit 1; }
	@set -e; \
	HASHEXE4=$$(md5sum build/pass4 | cut -d' ' -f1); \
	HASHEXE5=$$(md5sum build/pass5 | cut -d' ' -f1); \
	if [ "$$HASHEXE4" = "$$HASHEXE5" ]; then \
	    rm -f build/pass5; \
	    echo "Bootstrap complete (exe $$HASHEXE4)"; \
	    echo "============================================================"; \
	else \
	    echo "Bootstrap NOT converged: pass4/pass5 differ (exe: $$HASHEXE4 vs $$HASHEXE5)." >&2; \
	    exit 1; \
	fi
endif

# ── lsp ──────────────────────────────────────────────────────────────
ifeq ($(TARGET),win)
lsp:
	@# Windows LSP = the LspServerWin MONOLITH (LSP modules + the whole
	@# EmperorPenguinLib source set) — the dyn-lib pair is ELF-specific
	@# (SONAME/$$ORIGIN/rpath/-rdynamic); file namespaces only use basenames
	@# so ../..-relative sources are safe. No -enable-meta (the Linux LSP
	@# carries no JIT either). Windows coroutines & stdio events come from
	@# the runtime's fiber scheduler + PeekNamedPipe fd integration
	@# (EmperorPenguin/std/c/scheduler.c). On a linux host this cross-builds
	@# with the llvm-mingw toolchain; on a windows host it builds natively
	@# (CC/CXX/AR/CLANG/CROSS exported above).
	@echo "Building Windows LSP server (build/win64-lsp/MagellanicPenguinLSP.exe) ..."
	@echo "============================================================"
	@test -f build/pass4 || { echo "build/pass4 not found. Run 'make bootstrap' first." >&2; exit 1; }
	@mkdir -p build/win64-lsp
ifeq ($(HOST),linux)
	@set -o pipefail; { \
	$(XENV) OPT=-O2 build/pass4 MagellanicPenguin/LspServer/LspServerWin.penguins \
	    --enable-coroutine -target=win64 -o build/win64-lsp/MagellanicPenguinLSP.exe $(TEE) build/lsp-win.log; \
	} || { echo "Windows LSP build FAILED" >&2; exit 1; }
else
	@set -o pipefail; { \
	OPT=-O2 build/pass4 MagellanicPenguin/LspServer/LspServerWin.penguins \
	    --enable-coroutine -target=win64 -o build/win64-lsp/MagellanicPenguinLSP.exe $(TEE) build/lsp-win.log; \
	} || { echo "Windows LSP build FAILED" >&2; exit 1; }
	# The shared Tests/LspTest path expects build/lsp (the md files name it
	# in Run Args); the monolith is self-contained, no .penguin-lib needed.
	@cp -f build/win64-lsp/MagellanicPenguinLSP.exe build/lsp
endif
else
lsp:
	@# --enable-coroutine: the server is a ports/channels program end to end
	@# (StdioStream parks on fd 0/1, modules wait wires/Fifos). The compiler
	@# is consumed as libemperorpenguin.penguin-lib (EmperorPenguinLib.penguins):
	@# the LSP binary contains only the 10 LSP modules and monomorphizes its
	@# own generic instances from the lib's embedded source, so an LSP-only
	@# change relinks in seconds while compiler changes rebuild just the lib.
	@# e2e tests run the exe via the test runner's Prebuilt backend without
	@# recompiling (Tests/LspTest/*.md, Run Args=build/lsp).
	@#
	@# Content-addressed caches (LSP_NO_CACHE=1 forces rebuilds):
	@#   lib key  = pass3 + EmperorPenguinLib sources + auto-loaded stdlib pair
	@#   lsp key  = pass3 + LspServer sources + the lib artifact itself
	@# The lib lands NEXT TO build/lsp (link_exe stamps rpath $$ORIGIN + the
	@# lib's basename SONAME), so the pair is relocatable — `make publish`
	@# copies both.
	@echo "Building MagellanicPenguin LSP server (build/lsp + build/libemperorpenguin.penguin-lib) ..."
	@echo "============================================================"
	@test -f build/pass3 || { echo "build/pass3 not found. Run 'make bootstrap' first." >&2; exit 1; }
	@mkdir -p build
	@set -e; \
	LIB_KEY=""; \
	LIB_FRESH=0; \
	if [ -z "$$LSP_NO_CACHE" ]; then \
	    LIB_KEY_INPUT="build/pass3 EmperorPenguin/EmperorPenguinLib.penguins"; \
	    for f in $$(sed -n 's/^sources=\[\(.*\)\]/\1/p' EmperorPenguin/EmperorPenguinLib.penguins | tr ',' '\n' | sed 's/"//g; s/^[[:space:]]*//; s/[[:space:]]*$$//'); do \
	        if [ -f "EmperorPenguin/$$f" ]; then \
	            LIB_KEY_INPUT="$$LIB_KEY_INPUT EmperorPenguin/$$f"; \
	        fi; \
	    done; \
	    LIB_KEY_INPUT="$$LIB_KEY_INPUT EmperorPenguin/std/penguin/core_builtin.penguin EmperorPenguin/std/penguin/io.penguin"; \
	    LIB_KEY=$$(md5sum $$LIB_KEY_INPUT 2>/dev/null | md5sum | cut -d' ' -f1); \
	    if [ -n "$$LIB_KEY" ] && [ -f "build/lsp-cache/$$LIB_KEY/libemperorpenguin.penguin-lib" ]; then \
	        cp "build/lsp-cache/$$LIB_KEY/libemperorpenguin.penguin-lib" build/libemperorpenguin.penguin-lib; \
	        echo "Compiler lib cache hit ($$LIB_KEY) -> copied to build/libemperorpenguin.penguin-lib"; \
	        LIB_FRESH=1; \
	    fi; \
	fi; \
	if [ $$LIB_FRESH -eq 0 ]; then \
	    build/pass3 EmperorPenguin/EmperorPenguinLib.penguins -o build/libemperorpenguin.penguin-lib \
	        || { echo "LSP build FAILED at compiler lib stage" >&2; exit 1; }; \
	    if [ -n "$$LIB_KEY" ]; then \
	        mkdir -p "build/lsp-cache/$$LIB_KEY"; \
	        cp build/libemperorpenguin.penguin-lib "build/lsp-cache/$$LIB_KEY/libemperorpenguin.penguin-lib"; \
	        echo "Compiler lib cached as $$LIB_KEY"; \
	    fi; \
	fi; \
	LSP_KEY=""; \
	if [ -z "$$LSP_NO_CACHE" ]; then \
	    KEY_INPUT="build/pass3 MagellanicPenguin/LspServer/LspServer.penguins build/libemperorpenguin.penguin-lib"; \
	    for f in $$(sed -n 's/^sources=\[\(.*\)\]/\1/p' MagellanicPenguin/LspServer/LspServer.penguins | tr ',' '\n' | sed 's/"//g; s/^[[:space:]]*//; s/[[:space:]]*$$//'); do \
	        if [ -f "MagellanicPenguin/LspServer/$$f" ]; then \
	            KEY_INPUT="$$KEY_INPUT MagellanicPenguin/LspServer/$$f"; \
	        elif [ -f "$$f" ]; then \
	            KEY_INPUT="$$KEY_INPUT $$f"; \
	        fi; \
	    done; \
	    KEY_INPUT="$$KEY_INPUT EmperorPenguin/std/penguin/core_builtin.penguin EmperorPenguin/std/penguin/io.penguin"; \
	    LSP_KEY=$$(md5sum $$KEY_INPUT 2>/dev/null | md5sum | cut -d' ' -f1); \
	    if [ -n "$$LSP_KEY" ] && [ -f "build/lsp-cache/$$LSP_KEY/lsp" ]; then \
	        cp "build/lsp-cache/$$LSP_KEY/lsp" build/lsp; \
	        echo "LSP cache hit ($$LSP_KEY) -> copied to build/lsp"; \
	        exit 0; \
	    fi; \
	fi; \
	set -o pipefail; { \
	build/pass3 --enable-coroutine MagellanicPenguin/LspServer/LspServer.penguins \
	    --lib build/libemperorpenguin.penguin-lib -o build/lsp $(TEE) build/lsp.log; \
	} || exit 1; \
	if [ -n "$$LSP_KEY" ]; then \
	    mkdir -p "build/lsp-cache/$$LSP_KEY"; \
	    cp build/lsp "build/lsp-cache/$$LSP_KEY/lsp"; \
	    echo "LSP cached as $$LSP_KEY"; \
	fi
endif

# ── test / baseline_test ─────────────────────────────────────────────
test:
	@echo "Running PenguinLang Tests ..."
	@echo "============================================================"
	@mkdir -p build
	@set -o pipefail; { \
	dotnet run --configuration Release --project Tests/PenguinTestRunner.csproj -- $(TEST_ARGS) $(TEE) build/test.log; \
	} || { echo "Tests FAILED (full log: build/test.log)" >&2; exit 1; }

baseline_test:
	@echo "Running PenguinLang Tests (recording baseline) ..."
	@echo "============================================================"
	@mkdir -p build
	@set -o pipefail; { \
	dotnet run --configuration Release --project Tests/PenguinTestRunner.csproj -- --baseline $(TEST_ARGS) $(TEE) build/test.log; \
	} || { echo "Tests FAILED (full log: build/test.log)" >&2; exit 1; }

# ── publish ──────────────────────────────────────────────────────────
# TARGET=win / TARGET=linux builds just that platform; with no TARGET a
# linux host builds BOTH (the historical behavior), a windows host only win
# (win -> linux cross compiling is not supported).
ifeq ($(TARGET_DEFAULTED),1)
  ifeq ($(HOST),linux)
    PUBLISH_TARGETS := linux win
  else
    PUBLISH_TARGETS := win
  endif
else
  PUBLISH_TARGETS := $(TARGET)
endif

publish:
	@echo "Publishing self-contained executables + VSCode extension (targets: $(PUBLISH_TARGETS)) ..."
	@echo "============================================================"
	@test -f build/pass4 || { echo "build/pass4 not found. Run 'make bootstrap' first." >&2; exit 1; }
ifeq ($(findstring linux,$(PUBLISH_TARGETS)),linux)
	@echo "Building Linux EmperorPenguin (build/linux/emperor_penguin) ..."
	@mkdir -p build/linux
	@set -o pipefail; { \
	OPT=-O2 build/pass4 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -enable-meta -o build/linux/emperor_penguin -vv $(TEE) build/publish-linux.log; \
	} || { echo "Publish FAILED: Linux EmperorPenguin build" >&2; exit 1; }
	@cp build/linux/emperor_penguin MagellanicPenguin/vscode/server/linux/emperor_penguin
endif
ifeq ($(findstring win,$(PUBLISH_TARGETS)),win)
	@# CC/AR/CXX drive the libcore_builtin.a + (optionally) libpenguin_jit.a
	@# builds; CLANG selects the link-time compiler; -target=win64 tells
	@# main.penguin to link for PE/COFF (`-Wl,--stack` for the 32 MB PE
	@# stack). With a Windows LLVM available ($(WIN_META) when the package
	@# or LLVM_WIN_PREFIX is present) the win64 build also gets -enable-meta:
	@# the C runtime makefile builds libpenguin_jit.a for mingw and
	@# main.penguin links it against the DLL import lib (--export-all-symbols
	@# so the ORC JIT can resolve host functions). Without it the binary
	@# links no-op JIT stubs. lld-link infers the console subsystem from the
	@# `main` entry point, so no explicit subsystem flag is needed.
	@echo "Building Windows EmperorPenguin (build/win64/emperor_penguin.exe) ..."
	@mkdir -p build/win64
ifeq ($(HOST),linux)
	@set -o pipefail; { \
	$(XENV) OPT=-O2 build/pass4 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -o build/win64/emperor_penguin.exe -target=win64 $(WIN_META) -vv $(TEE) build/publish-win64.log; \
	} || { echo "Publish FAILED: Windows EmperorPenguin cross-compile" >&2; exit 1; }
else
	@set -o pipefail; { \
	OPT=-O2 build/pass4 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -o build/win64/emperor_penguin.exe -target=win64 $(WIN_META) -vv $(TEE) build/publish-win64.log; \
	} || { echo "Publish FAILED: Windows EmperorPenguin build" >&2; exit 1; }
endif
	@cp build/win64/emperor_penguin.exe MagellanicPenguin/vscode/server/windows/emperor_penguin.exe
endif
ifeq ($(findstring linux,$(PUBLISH_TARGETS)),linux)
	@# --- PenguinLang-native LSP server (linux), built by `make lsp` ---
	@# The extension's client picks PENGUINLANG_LSPSERVER_PATH first, else the
	@# bundled server/<platform>/MagellanicPenguinLSP binary. The compiler lib
	@# the exe links (SONAME libemperorpenguin.penguin-lib, rpath $$ORIGIN)
	@# must sit BESIDE the binary in the deployed layout, and the stdlib is
	@# bundled at <exe_dir>/EmperorPenguin/std/penguin (one of
	@# load_stdlib_text's candidates), so an installed extension analyzes
	@# documents without the repo.
	@if [ -f build/lsp ]; then \
	    if [ ! -f build/libemperorpenguin.penguin-lib ]; then \
	        echo "Publish FAILED: build/lsp exists but build/libemperorpenguin.penguin-lib is missing (the exe links it via its rpath). Run 'make lsp' first." >&2; \
	        exit 1; \
	    fi; \
	    mkdir -p MagellanicPenguin/vscode/server/linux; \
	    cp build/lsp MagellanicPenguin/vscode/server/linux/MagellanicPenguinLSP; \
	    echo "Copied build/lsp -> MagellanicPenguin/vscode/server/linux/MagellanicPenguinLSP"; \
	    cp build/libemperorpenguin.penguin-lib MagellanicPenguin/vscode/server/linux/libemperorpenguin.penguin-lib; \
	    echo "Copied build/libemperorpenguin.penguin-lib -> MagellanicPenguin/vscode/server/linux/"; \
	    mkdir -p MagellanicPenguin/vscode/server/linux/EmperorPenguin/std; \
	    cp -r EmperorPenguin/std/penguin MagellanicPenguin/vscode/server/linux/EmperorPenguin/std/penguin; \
	    echo "Bundled stdlib -> MagellanicPenguin/vscode/server/linux/EmperorPenguin/std/penguin"; \
	    # Smoke test the BUNDLED server from a foreign cwd (no repo): a full \
	    # session must answer capabilities, survive opening a broken document \
	    # (the parser-panic path exercises the sjlj try/catch landing pad — \
	    # publishing an 'internal compiler error' diagnostic, not dying) and \
	    # exit 0 — proving the exe-dir stdlib discovery AND the rpath \
	    # lib load work in a deployed layout. \
	    SMOKE_DIR=$$(mktemp -d); \
	    REPO_ROOT=$$PWD; \
	    printf 'Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 172\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///smoke/broken.penguin","languageId":"penguin","version":1,"text":"fun broken( {"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}' \
	        | (cd "$$SMOKE_DIR" && "$$REPO_ROOT/MagellanicPenguin/vscode/server/linux/MagellanicPenguinLSP" > out.bin 2> err.bin); \
	    SMOKE_RC=$$?; \
	    if [ $$SMOKE_RC -ne 0 ] || ! grep -q "definitionProvider" "$$SMOKE_DIR/out.bin" 2>/dev/null \
	        || ! grep -q "internal compiler error" "$$SMOKE_DIR/out.bin" 2>/dev/null; then \
	        echo "Publish FAILED: bundled LSP smoke test (exit=$$SMOKE_RC)" >&2; \
	        cat "$$SMOKE_DIR/err.bin" >&2; \
	        rm -rf "$$SMOKE_DIR"; \
	        exit 1; \
	    fi; \
	    rm -rf "$$SMOKE_DIR"; \
	    echo "Bundled LSP smoke test passed (initialize + broken-doc survival + shutdown/exit from a foreign cwd)"; \
	else \
	    echo "build/lsp not found; skipping native LSP server (build with 'make lsp')"; \
	fi
endif
ifeq ($(findstring win,$(PUBLISH_TARGETS)),win)
	@# --- PenguinLang-native LSP server (windows), built by `make lsp TARGET=win` ---
	@# Same deployed layout as the linux side: the exe plus the stdlib bundle
	@# beside it (the server's embedded document compiles resolve stdlib via
	@# the exe-dir candidate on every platform). The Windows binary is a
	@# self-contained monolith — no .penguin-lib to ship. The smoke runs a
	@# full session (initialize, a broken-document didOpen exercising the
	@# Windows sjlj landing pad, shutdown/exit) from a foreign cwd — under
	@# wine on a linux host (WINE=<path> or wine on PATH), natively on
	@# windows.
	@if [ -f build/win64-lsp/MagellanicPenguinLSP.exe ]; then \
	    cp build/win64-lsp/MagellanicPenguinLSP.exe MagellanicPenguin/vscode/server/windows/MagellanicPenguinLSP.exe; \
	    echo "Copied build/win64-lsp/MagellanicPenguinLSP.exe -> MagellanicPenguin/vscode/server/windows/"; \
	    mkdir -p MagellanicPenguin/vscode/server/windows/EmperorPenguin/std; \
	    cp -r EmperorPenguin/std/penguin MagellanicPenguin/vscode/server/windows/EmperorPenguin/std/penguin; \
	    echo "Bundled stdlib -> MagellanicPenguin/vscode/server/windows/EmperorPenguin/std/penguin"; \
	    SMOKE_DIR=$$(mktemp -d); \
	    REPO_ROOT=$$PWD; \
	    SERVER="$$REPO_ROOT/MagellanicPenguin/vscode/server/windows/MagellanicPenguinLSP.exe"; \
	    RUNNER=""; \
	    if [ "$(HOST)" = "linux" ]; then \
	        WINE_BIN="$${WINE:-wine}"; \
	        if command -v "$$WINE_BIN" >/dev/null 2>&1; then \
	            RUNNER="$$WINE_BIN"; \
	        fi; \
	    else \
	        RUNNER=1; \
	    fi; \
	    if [ -n "$$RUNNER" ]; then \
	        if [ "$$RUNNER" = "1" ]; then RUNNER="$$SERVER"; SERVER=""; fi; \
	        printf 'Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 172\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///smoke/broken.penguin","languageId":"penguin","version":1,"text":"fun broken( {"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}' \
	            | (cd "$$SMOKE_DIR" && $$RUNNER $$SERVER > out.bin 2> err.bin); \
	        SMOKE_RC=$$?; \
	        if [ $$SMOKE_RC -ne 0 ] || ! grep -q "definitionProvider" "$$SMOKE_DIR/out.bin" 2>/dev/null \
	            || ! grep -q "internal compiler error" "$$SMOKE_DIR/out.bin" 2>/dev/null; then \
	            echo "Publish FAILED: Windows LSP smoke test (exit=$$SMOKE_RC)" >&2; \
	            cat "$$SMOKE_DIR/err.bin" >&2; \
	            rm -rf "$$SMOKE_DIR"; \
	            exit 1; \
	        fi; \
	        echo "Windows LSP smoke test passed (initialize + broken-doc survival + shutdown/exit, foreign cwd)"; \
	    else \
	        echo "(no wine available: skipped the Windows LSP runtime smoke test — the binary is cross-compile/link-validated only; run it on a Windows host or set WINE=<path>)"; \
	    fi; \
	    rm -rf "$$SMOKE_DIR"; \
	else \
	    echo "build/win64-lsp/MagellanicPenguinLSP.exe not found; skipping Windows LSP server (build with 'make lsp TARGET=win')"; \
	fi
	@# Windows meta JIT runtime DLLs ride along in the vscode windows server dir
	@# (only the three LLVM DLLs are vendored in thirdparty/.../bin; their own
	@# deps — libc++/libffi/zlib/zstd/libxml2/winpthread — must be staged from
	@# the MSYS2 clang64 bin for the meta exe to actually run).
	@if [ -d "thirdparty/mingw-w64-x86_64-llvm-libs/bin" ]; then \
	    cp thirdparty/mingw-w64-x86_64-llvm-libs/bin/*.dll MagellanicPenguin/vscode/server/windows/; \
	fi
endif
	@# Publish native, self-contained dotnet executables for the selected targets.
ifeq ($(findstring linux,$(PUBLISH_TARGETS)),linux)
	@dotnet publish -r linux-x64 --self-contained || { echo "Publish FAILED: linux-x64 self-contained" >&2; exit 1; }
endif
ifeq ($(findstring win,$(PUBLISH_TARGETS)),win)
	@dotnet publish -r win-x64 --self-contained || { echo "Publish FAILED: win-x64 self-contained" >&2; exit 1; }
endif
	@# Build the VSCode extension package (subshell keeps this makefile's cwd intact).
	@(cd MagellanicPenguin/vscode && npm run package) || { echo "Publish FAILED: vscode extension (npm run package)" >&2; exit 1; }
	@echo "Publish complete"
