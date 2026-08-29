# PenguinLang root Makefile
# =====================================================================
# All build artifacts live under build/ (gitignored).
#
#   make clean              remove the build/ tree
#   make bootstrap          self-bootstrap EmperorPenguin (see layout below)
#   make release            release the native .ll emitter (host platform)
#   make release_linux      linux emitter+script+dynlib -> build/linux/
#   make release_win        windows emitter+script -> build/win/
#                           (cross compiled from a linux host by the
#                           emperor script; native on a windows host)
#   make lsp                PenguinLang-native LSP server (host platform)
#   make lsp_linux          build/lsp + build/libemperorpenguin.penguin-lib
#                           (reuses the release dynlib)
#   make lsp_win            build/win/MagellanicPenguinLSP.exe monolith
#   make test               run the cross-compiler markdown suite (Tests/*.md)
#   make baseline_test      like `test`, but record the run as the new baseline
#   make publish            deploy release+LSP into the vscode extension,
#                           dotnet self-contained publishes, vsix package
#   make all                bootstrap + lsp + test, in that order (default)
#
# Bootstrap and test always target the HOST (never cross). Cross compiling
# is linux->win only and is expressed by the explicit *_win targets; the
# `emperor` driver script owns the cross toolchain selection.
#
# Variables (overridable via environment or make command line):
#   TEST_ARGS="..."        extra args for `make test` / `make baseline_test`
#                          (e.g. TEST_ARGS="--filter LspTest/*")
#   WINE=<wine binary>     wine used for the windows publish smoke test on a
#                          linux host (on a windows host the smoke runs natively)
#   MINGW_PREFIX, WIN_CC, WIN_CXX, WIN_AR, WIN_CLANG
#                          llvm-mingw cross toolchain (linux -> win); forwarded
#                          to the emperor script
#   LLVM_WIN_PREFIX        dir with a Windows LLVM for the meta JIT (needs
#                          lib/libLLVM-22.dll.a + include/); defaults to
#                          thirdparty/mingw-w64-x86_64-llvm-libs when present.
#                          REQUIRED for `bootstrap`/`release`/`publish` on a
#                          windows host (every bootstrap stage links the JIT).
#
# Linking is external: the compiler binary only emits platform-independent
# LLVM IR (build/**/<name>.ll + side files); EmperorPenguin/emperor runs
# clang + the C-runtime make. The same .ll links for every platform, so
# release_linux / release_win share build/release/*.ll and only differ in
# the link and the C runtime archive.
#
# Bootstrap layout (numbering unchanged from the old build/passN scheme;
# every stage is a FILE target with file-level dependencies — unchanged
# sources are never recompiled):
#   pass1  BabyPenguin (dotnet, --backend=cs) emits $(BS)/pass2.ll; the
#          emperor script links it -> build/bootstrap/pass2
#   pass2  build/bootstrap/pass2 (Full monolith) -> build/bootstrap/pass3
#   linux: pass3/pass4/pass5 are lib+exe pairs (EmperorPenguinLib/Exe
#          projects) under passN.d/ — pass5 exists only for the md5
#          convergence check (exe AND lib); build/bootstrap/pass4 is a
#          symlink into build/bootstrap/pass4.d
#   win:   the .penguin-lib pair is ELF-specific (SONAME/$ORIGIN/rpath/
#          -rdynamic), so pass2..pass5 stay Full monoliths and the
#          convergence check compares the exe md5s only
#   The convergence artifacts (pass5.d / pass5) are KEPT after a successful
#   check — a repeat `make bootstrap` with unchanged inputs only re-verifies
#   the md5s instead of recompiling.
# =====================================================================

ifeq ($(OS),Windows_NT)
  HOST := win
else
  HOST := linux
endif

.DEFAULT_GOAL := all
.PHONY: all clean bootstrap lsp release test baseline_test publish \
        release_linux release_win lsp_linux lsp_win

BS := build/bootstrap
REL := build/release

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

# ── Cross toolchain (linux -> win); forwarded to the emperor script ───
export MINGW_PREFIX ?= /opt/llvm-mingw
export WIN_CC    ?= $(MINGW_PREFIX)/bin/x86_64-w64-mingw32-clang
export WIN_CXX   ?= $(MINGW_PREFIX)/bin/x86_64-w64-mingw32-clang++
export WIN_AR    ?= $(MINGW_PREFIX)/bin/x86_64-w64-mingw32-ar
export WIN_CLANG ?= $(WIN_CC)

# ── Windows LLVM for the meta JIT ────────────────────────────────────
# Auto-detected from the vendored package (win32 cross from linux, and the
# native-windows flow — the windows meta link needs exactly this layout:
# <prefix>/lib/libLLVM-22.dll.a + <prefix>/include). Forwarded to the
# emperor script.
LLVM_WIN_PREFIX ?= $(if $(wildcard thirdparty/mingw-w64-x86_64-llvm-libs/lib/libLLVM-22.dll.a),$(abspath thirdparty/mingw-w64-x86_64-llvm-libs),)
ifneq ($(LLVM_WIN_PREFIX),)
  export LLVM_WIN_PREFIX
endif

# ── Native windows host ──────────────────────────────────────────────
# MSYS2-style environment (make/bash/clang/llvm-ar on PATH). Every
# -enable-meta binary loads libLLVM-22.dll at process start; the vendored
# DLLs live in <pkg>/bin (their own CRT deps come from the MSYS2 toolchain
# bin dirs already on PATH). Tool selection for the C runtime build and the
# link lives in the emperor script (it forces CC=clang/CROSS=win64 there).
ifeq ($(HOST),win)
  ifneq ($(LLVM_WIN_PREFIX),)
    export PATH := $(LLVM_WIN_PREFIX)/bin:$(PATH)
  else
    GOALS := $(if $(MAKECMDGOALS),$(MAKECMDGOALS),all)
    ifneq ($(filter all bootstrap lsp release publish,$(GOALS)),)
      $(error native windows bootstrap/lsp/release/publish needs a Windows LLVM for -enable-meta: vendor thirdparty/mingw-w64-x86_64-llvm-libs (see its README) or set LLVM_WIN_PREFIX=<dir with lib/libLLVM-22.dll.a>)
    endif
  endif
endif

# ── Source dependencies (file-level; unchanged files are not recompiled) ──
# sources=[...] entries of a .penguins project file.
proj_sources = $(shell sed -n 's/^sources=\[\(.*\)\]/\1/p' $(1) | tr ',' '\n' | sed 's/"//g; s/^[[:space:]]*//; s/[[:space:]]*$$//')

# Auto-loaded stdlib pair (read CWD-relative by every compiler invocation).
EP_STD := EmperorPenguin/std/penguin/core_builtin.penguin EmperorPenguin/std/penguin/io.penguin

# C runtime + driver script: a change here only re-LINKS, never re-emits.
# Explicit source patterns (never a bare *) so stale .o/.a leftovers in the
# tree cannot sneak into the dependency graph as make-implicit-rule targets.
C_RT := $(wildcard EmperorPenguin/std/c/*.c EmperorPenguin/std/c/*.cpp EmperorPenguin/std/c/*.h) \
        EmperorPenguin/std/c/Makefile \
        $(wildcard EmperorPenguin/std/include/*) \
        EmperorPenguin/emperor
ifeq ($(HOST),win)
  C_RT += EmperorPenguin/emperor.bat
endif

BABY_CS := $(shell find BabyPenguin PenguinLangParser -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null) \
           BabyPenguin/BabyPenguin.csproj PenguinLangParser/PenguinLangParser.csproj

EP1_SRC := EmperorPenguin/EmperorPenguinPass1.penguins \
           $(addprefix EmperorPenguin/,$(call proj_sources,EmperorPenguin/EmperorPenguinPass1.penguins)) \
           EmperorPenguin/src/utils.penguin
EP2_SRC := EmperorPenguin/EmperorPenguinPass2.penguins \
           $(addprefix EmperorPenguin/,$(call proj_sources,EmperorPenguin/EmperorPenguinPass2.penguins))
EPLIB_SRC := EmperorPenguin/EmperorPenguinLib.penguins \
           $(addprefix EmperorPenguin/,$(call proj_sources,EmperorPenguin/EmperorPenguinLib.penguins))
EPEXE_SRC := EmperorPenguin/EmperorPenguinExe.penguins \
           $(addprefix EmperorPenguin/,$(call proj_sources,EmperorPenguin/EmperorPenguinExe.penguins))
LSP_SRC := MagellanicPenguin/LspServer/LspServer.penguins \
           $(addprefix MagellanicPenguin/LspServer/,$(call proj_sources,MagellanicPenguin/LspServer/LspServer.penguins))
# LspServerWin mixes same-dir LSP modules with ../../-relative EmperorPenguin sources.
LSPWIN_SRC := MagellanicPenguin/LspServer/LspServerWin.penguins \
           $(foreach f,$(call proj_sources,MagellanicPenguin/LspServer/LspServerWin.penguins),\
             $(if $(filter ../../%,$(f)),$(patsubst ../../%,%,$(f)),MagellanicPenguin/LspServer/$(f)))

# ── all ──────────────────────────────────────────────────────────────
# Recursive sub-makes give strict bootstrap -> lsp -> test ordering without
# declaring false dependencies.
all:
	@$(MAKE) bootstrap
	@$(MAKE) lsp
	@$(MAKE) test

# ── clean ────────────────────────────────────────────────────────────
clean:
	@echo "Cleaning build artifacts ..."
	rm -rf build

# ── bootstrap ────────────────────────────────────────────────────────
ifeq ($(HOST),linux)

# pass1 (dotnet cs backend) emits pass2.ll; the emperor script links it.
# -enable-meta is a LINK flag (JIT archives into the produced binary) — the
# emission itself is target- and link-independent.
$(BS)/pass2.ll: $(BABY_CS) $(EP1_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Bootstrap pass1: BabyPenguin --backend=cs -> $@"
	@set -o pipefail; { \
	dotnet run --configuration Release --project BabyPenguin -- \
	    --backend=cs EmperorPenguin/EmperorPenguinPass1.penguins -- \
	    EmperorPenguin/EmperorPenguinPass1.penguins EmperorPenguin/src/utils.penguin \
	    --disable-dl -vv -o $(BS)/pass2 $(TEE) build/logs/pass1.log; \
	} || { echo "Bootstrap FAILED at pass1 emission (BabyPenguin cs backend -> $(BS)/pass2.ll)" >&2; exit 1; }

$(BS)/pass2: $(BS)/pass2.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link $(BS)/pass2.ll -o $(BS)/pass2 \
	    -enable-meta $(TEE) build/logs/pass1-link.log; \
	} || { echo "Bootstrap FAILED at pass1 link -> $(BS)/pass2" >&2; exit 1; }

# pass2 (Full monolith) builds pass3: the json-backed Dynlib + json/vector/
# hashmap + _utils are part of the compiler, making pass3 the first
# dyn-lib-capable compiler. --enable-coroutine is passed ONLY here: pass3 is
# the first coroutine-capable compiler (ports/channels/scheduler syntax
# accepted). Wait-free programs (the compiler itself included) emit identical
# binaries with or without the flag.
$(BS)/pass3.ll: $(BS)/pass2 $(EP2_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Bootstrap pass2: $(BS)/pass2 -> $@"
	@set -o pipefail; { \
	$(BS)/pass2 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv --enable-coroutine -o $(BS)/pass3 $(TEE) build/logs/pass2.log; \
	} || { echo "Bootstrap FAILED at pass2 emission -> $(BS)/pass3.ll" >&2; exit 1; }

$(BS)/pass3: $(BS)/pass3.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link $(BS)/pass3.ll -o $(BS)/pass3 \
	    -enable-meta $(TEE) build/logs/pass2-link.log; \
	} || { echo "Bootstrap FAILED at pass2 link -> $(BS)/pass3" >&2; exit 1; }

# pass3 -> pass4: split lib+exe (EmperorPenguinLib/Exe projects). The whole
# compiler is a libemperorpenguin.penguin-lib and the CLI driver links it,
# making dyn-lib load-bearing at compiler scale — every bootstrap exercises
# the .penguin-lib build/consume path on the compiler's own 16k lines. The
# exe finds its lib via rpath $$ORIGIN (SONAME = lib basename); $(BS)/pass4
# is a symlink into pass4.d so the conventional path keeps working from any
# cwd. Lib-mode emission writes .ll + .libmeta (grouped target).
$(BS)/pass4.d/libemperorpenguin.ll $(BS)/pass4.d/libemperorpenguin.libmeta \
        &: $(BS)/pass3 $(EPLIB_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Bootstrap pass3 (lib): $(BS)/pass3 -> $(BS)/pass4.d/libemperorpenguin.ll"
	@set -o pipefail; { \
	$(BS)/pass3 EmperorPenguin/EmperorPenguinLib.penguins \
	    -vv -o $(BS)/pass4.d/libemperorpenguin.penguin-lib $(TEE) build/logs/pass3-lib.log; \
	} || { echo "Bootstrap FAILED at pass3 lib emission" >&2; exit 1; }

$(BS)/pass4.d/libemperorpenguin.penguin-lib: $(BS)/pass4.d/libemperorpenguin.ll $(BS)/pass4.d/libemperorpenguin.libmeta $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link-lib $(BS)/pass4.d/libemperorpenguin.ll \
	    $(BS)/pass4.d/libemperorpenguin.libmeta \
	    -o $(BS)/pass4.d/libemperorpenguin.penguin-lib $(TEE) build/logs/pass3-liblink.log; \
	} || { echo "Bootstrap FAILED at pass3 lib link" >&2; exit 1; }

$(BS)/pass4.d/pass4.ll: $(BS)/pass3 $(BS)/pass4.d/libemperorpenguin.penguin-lib $(EPEXE_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Bootstrap pass3 (exe): $(BS)/pass3 -> $(BS)/pass4.d/pass4.ll"
	@set -o pipefail; { \
	$(BS)/pass3 EmperorPenguin/EmperorPenguinExe.penguins \
	    -vv --lib $(BS)/pass4.d/libemperorpenguin.penguin-lib \
	    -o $(BS)/pass4.d/pass4 $(TEE) build/logs/pass3.log; \
	} || { echo "Bootstrap FAILED at pass3 exe emission" >&2; exit 1; }

$(BS)/pass4: $(BS)/pass4.d/pass4.ll $(BS)/pass4.d/libemperorpenguin.penguin-lib $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link $(BS)/pass4.d/pass4.ll -o $(BS)/pass4.d/pass4 \
	    -enable-meta --consumer-lib $(BS)/pass4.d/libemperorpenguin.penguin-lib \
	    $(TEE) build/logs/pass3-exelink.log; \
	} || { echo "Bootstrap FAILED at pass3 exe link" >&2; exit 1; }
	@ln -sf pass4.d/pass4 $(BS)/pass4

# pass4 -> pass5: convergence-only repeat of the lib+exe stage. Kept after a
# successful check so a repeat bootstrap with unchanged inputs re-verifies
# md5s without recompiling.
$(BS)/pass5.d/libemperorpenguin.ll $(BS)/pass5.d/libemperorpenguin.libmeta \
        &: $(BS)/pass4 $(EPLIB_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Bootstrap pass4 (lib, convergence): $(BS)/pass4 -> $(BS)/pass5.d/libemperorpenguin.ll"
	@set -o pipefail; { \
	$(BS)/pass4 EmperorPenguin/EmperorPenguinLib.penguins \
	    -vv -o $(BS)/pass5.d/libemperorpenguin.penguin-lib $(TEE) build/logs/pass4-lib.log; \
	} || { echo "Bootstrap FAILED at pass4 lib emission" >&2; exit 1; }

$(BS)/pass5.d/libemperorpenguin.penguin-lib: $(BS)/pass5.d/libemperorpenguin.ll $(BS)/pass5.d/libemperorpenguin.libmeta $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link-lib $(BS)/pass5.d/libemperorpenguin.ll \
	    $(BS)/pass5.d/libemperorpenguin.libmeta \
	    -o $(BS)/pass5.d/libemperorpenguin.penguin-lib $(TEE) build/logs/pass4-liblink.log; \
	} || { echo "Bootstrap FAILED at pass4 lib link" >&2; exit 1; }

$(BS)/pass5.d/pass5.ll: $(BS)/pass4 $(BS)/pass5.d/libemperorpenguin.penguin-lib $(EPEXE_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Bootstrap pass4 (exe, convergence): $(BS)/pass4 -> $(BS)/pass5.d/pass5.ll"
	@set -o pipefail; { \
	$(BS)/pass4 EmperorPenguin/EmperorPenguinExe.penguins \
	    -vv --lib $(BS)/pass5.d/libemperorpenguin.penguin-lib \
	    -o $(BS)/pass5.d/pass5 $(TEE) build/logs/pass4.log; \
	} || { echo "Bootstrap FAILED at pass4 exe emission" >&2; exit 1; }

$(BS)/pass5: $(BS)/pass5.d/pass5.ll $(BS)/pass5.d/libemperorpenguin.penguin-lib $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link $(BS)/pass5.d/pass5.ll -o $(BS)/pass5.d/pass5 \
	    -enable-meta --consumer-lib $(BS)/pass5.d/libemperorpenguin.penguin-lib \
	    $(TEE) build/logs/pass4-exelink.log; \
	} || { echo "Bootstrap FAILED at pass4 exe link" >&2; exit 1; }
	@ln -sf pass5.d/pass5 $(BS)/pass5

bootstrap: $(BS)/pass4 $(BS)/pass5
	@echo "Checking bootstrap convergence (pass4 vs pass5) ..."
	@set -e; \
	if command -v md5sum >/dev/null 2>&1; then \
	    HASHEXE4=$$(md5sum $(BS)/pass4.d/pass4 | cut -d' ' -f1); \
	    HASHEXE5=$$(md5sum $(BS)/pass5.d/pass5 | cut -d' ' -f1); \
	    HASHLIB4=$$(md5sum $(BS)/pass4.d/libemperorpenguin.penguin-lib | cut -d' ' -f1); \
	    HASHLIB5=$$(md5sum $(BS)/pass5.d/libemperorpenguin.penguin-lib | cut -d' ' -f1); \
	else \
	    HASHEXE4=$$(md5 -q $(BS)/pass4.d/pass4); \
	    HASHEXE5=$$(md5 -q $(BS)/pass5.d/pass5); \
	    HASHLIB4=$$(md5 -q $(BS)/pass4.d/libemperorpenguin.penguin-lib); \
	    HASHLIB5=$$(md5 -q $(BS)/pass5.d/libemperorpenguin.penguin-lib); \
	fi; \
	if [ "$$HASHEXE4" = "$$HASHEXE5" ] && [ "$$HASHLIB4" = "$$HASHLIB5" ]; then \
	    echo "Bootstrap complete (exe $$HASHEXE4, lib $$HASHLIB4)"; \
	    echo "============================================================"; \
	else \
	    echo "Bootstrap NOT converged: pass4/pass5 differ (exe: $$HASHEXE4 vs $$HASHEXE5, lib: $$HASHLIB4 vs $$HASHLIB5)." >&2; \
	    exit 1; \
	fi

else
# ── native windows: Full-monolith chain (no .penguin-lib — ELF-only) ──

$(BS)/pass2.ll: $(BABY_CS) $(EP1_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Bootstrap pass1: BabyPenguin --backend=cs -> $@"
	@set -o pipefail; { \
	dotnet run --configuration Release --project BabyPenguin -- \
	    --backend=cs EmperorPenguin/EmperorPenguinPass1.penguins -- \
	    EmperorPenguin/EmperorPenguinPass1.penguins EmperorPenguin/src/utils.penguin \
	    --disable-dl -vv -o $(BS)/pass2 $(TEE) build/logs/pass1.log; \
	} || { echo "Bootstrap FAILED at pass1 emission (BabyPenguin cs backend -> $(BS)/pass2.ll)" >&2; exit 1; }

$(BS)/pass2: $(BS)/pass2.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link $(BS)/pass2.ll -o $(BS)/pass2 \
	    -enable-meta -target=win64 $(TEE) build/logs/pass1-link.log; \
	} || { echo "Bootstrap FAILED at pass1 link -> $(BS)/pass2" >&2; exit 1; }

$(BS)/pass3.ll: $(BS)/pass2 $(EP2_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	$(BS)/pass2 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv --enable-coroutine -o $(BS)/pass3 $(TEE) build/logs/pass2.log; \
	} || { echo "Bootstrap FAILED at pass2 emission -> $(BS)/pass3.ll" >&2; exit 1; }

$(BS)/pass3: $(BS)/pass3.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link $(BS)/pass3.ll -o $(BS)/pass3 \
	    -enable-meta -target=win64 $(TEE) build/logs/pass2-link.log; \
	} || { echo "Bootstrap FAILED at pass2 link -> $(BS)/pass3" >&2; exit 1; }

$(BS)/pass4.ll: $(BS)/pass3 $(EP2_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	$(BS)/pass3 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv -o $(BS)/pass4 $(TEE) build/logs/pass3.log; \
	} || { echo "Bootstrap FAILED at pass3 emission -> $(BS)/pass4.ll" >&2; exit 1; }

$(BS)/pass4: $(BS)/pass4.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link $(BS)/pass4.ll -o $(BS)/pass4 \
	    -enable-meta -target=win64 $(TEE) build/logs/pass3-link.log; \
	} || { echo "Bootstrap FAILED at pass3 link -> $(BS)/pass4" >&2; exit 1; }

$(BS)/pass5.ll: $(BS)/pass4 $(EP2_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	$(BS)/pass4 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv -o $(BS)/pass5 $(TEE) build/logs/pass4.log; \
	} || { echo "Bootstrap FAILED at pass4 emission -> $(BS)/pass5.ll" >&2; exit 1; }

$(BS)/pass5: $(BS)/pass5.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	EmperorPenguin/emperor link $(BS)/pass5.ll -o $(BS)/pass5 \
	    -enable-meta -target=win64 $(TEE) build/logs/pass4-link.log; \
	} || { echo "Bootstrap FAILED at pass4 link -> $(BS)/pass5" >&2; exit 1; }

bootstrap: $(BS)/pass4 $(BS)/pass5
	@echo "Checking bootstrap convergence (pass4 vs pass5) ..."
	@set -e; \
	HASHEXE4=$$(md5sum $(BS)/pass4 | cut -d' ' -f1); \
	HASHEXE5=$$(md5sum $(BS)/pass5 | cut -d' ' -f1); \
	if [ "$$HASHEXE4" = "$$HASHEXE5" ]; then \
	    echo "Bootstrap complete (exe $$HASHEXE4)"; \
	    echo "============================================================"; \
	else \
	    echo "Bootstrap NOT converged: pass4/pass5 differ (exe: $$HASHEXE4 vs $$HASHEXE5)." >&2; \
	    exit 1; \
	fi
endif

# ── release ──────────────────────────────────────────────────────────
# The emitted IR is platform-independent: ONE emission serves every link
# target. build/release/*.ll (+ .def / .libmeta side files) is shared by
# release_linux and release_win; each platform only re-links against its own
# C runtime archive.
#
# The deployed compiler is the emitter binary + the emperor driver script:
# the script locates the tree (EmperorPenguin/std) beside itself, checks the
# LLVM environment, runs the emitter and links.

$(REL)/emperor_penguin_llvmir_emitter.ll $(REL)/emperor_penguin_llvmir_emitter.def \
        &: $(BS)/pass4 $(EP2_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Release: emitting $(REL)/emperor_penguin_llvmir_emitter.ll"
	@set -o pipefail; { \
	$(BS)/pass4 EmperorPenguin/EmperorPenguinPass2.penguins \
	    -vv -o $(REL)/emperor_penguin_llvmir_emitter $(TEE) build/logs/release-emitter.log; \
	} || { echo "Release FAILED: emitter emission" >&2; exit 1; }

# The compiler-as-dynlib for the LSP (no meta — the consumer LSP exe carries
# no JIT either, matching the pre-split LSP builds).
$(REL)/libemperorpenguin.ll $(REL)/libemperorpenguin.libmeta \
        &: $(BS)/pass4 $(EPLIB_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "Release: emitting $(REL)/libemperorpenguin.ll"
	@set -o pipefail; { \
	$(BS)/pass4 EmperorPenguin/EmperorPenguinLib.penguins \
	    -vv -o $(REL)/libemperorpenguin.penguin-lib $(TEE) build/logs/release-lib.log; \
	} || { echo "Release FAILED: dynlib emission" >&2; exit 1; }

build/linux/emperor_penguin_llvmir_emitter: $(REL)/emperor_penguin_llvmir_emitter.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	OPT=-O2 EmperorPenguin/emperor link $(REL)/emperor_penguin_llvmir_emitter.ll \
	    -o build/linux/emperor_penguin_llvmir_emitter \
	    -enable-meta $(TEE) build/logs/release-linux.log; \
	} || { echo "Release FAILED: linux emitter link" >&2; exit 1; }

build/linux/libemperorpenguin.penguin-lib: $(REL)/libemperorpenguin.ll $(REL)/libemperorpenguin.libmeta $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	OPT=-O2 EmperorPenguin/emperor link-lib $(REL)/libemperorpenguin.ll $(REL)/libemperorpenguin.libmeta \
	    -o build/linux/libemperorpenguin.penguin-lib $(TEE) build/logs/release-linux-lib.log; \
	} || { echo "Release FAILED: linux dynlib link" >&2; exit 1; }

build/linux/emperor: EmperorPenguin/emperor
	@mkdir -p $(@D)
	@cp -f EmperorPenguin/emperor $@

release_linux: build/linux/emperor_penguin_llvmir_emitter build/linux/libemperorpenguin.penguin-lib build/linux/emperor

build/win/emperor_penguin_llvmir_emitter.exe: $(REL)/emperor_penguin_llvmir_emitter.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	OPT=-O2 EmperorPenguin/emperor link $(REL)/emperor_penguin_llvmir_emitter.ll \
	    -o build/win/emperor_penguin_llvmir_emitter.exe \
	    -enable-meta -target=win64 $(TEE) build/logs/release-win.log; \
	} || { echo "Release FAILED: windows emitter link" >&2; exit 1; }

build/win/emperor.bat: EmperorPenguin/emperor.bat
	@mkdir -p $(@D)
	@cp -f EmperorPenguin/emperor.bat $@

release_win: build/win/emperor_penguin_llvmir_emitter.exe build/win/emperor.bat

release: release_$(HOST)
	@echo "Release complete ($(HOST))"

# ── lsp ──────────────────────────────────────────────────────────────
# Linux: the LSP exe links the RELEASE dynlib (same compiler build as the
# deployed emitter). The lib lands NEXT TO build/lsp (rpath $$ORIGIN + the
# lib's basename SONAME), so the pair is relocatable and the shared
# Tests/LspTest path (`Args: build/lsp`) keeps working — the test runner
# copies any sibling *.penguin-lib beside the exe it stages.
build/libemperorpenguin.penguin-lib: build/linux/libemperorpenguin.penguin-lib
	@mkdir -p $(@D)
	@cp -f $< $@

build/lsp.ll: $(BS)/pass4 build/libemperorpenguin.penguin-lib $(LSP_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "LSP: emitting build/lsp.ll"
	@set -o pipefail; { \
	$(BS)/pass4 --enable-coroutine MagellanicPenguin/LspServer/LspServer.penguins \
	    --lib build/libemperorpenguin.penguin-lib -vv -o build/lsp $(TEE) build/logs/lsp.log; \
	} || { echo "LSP build FAILED at emission" >&2; exit 1; }

build/lsp: build/lsp.ll build/libemperorpenguin.penguin-lib $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	OPT=-O2 EmperorPenguin/emperor link build/lsp.ll -o build/lsp \
	    --consumer-lib build/libemperorpenguin.penguin-lib $(TEE) build/logs/lsp-link.log; \
	} || { echo "LSP build FAILED at link" >&2; exit 1; }

lsp_linux: build/lsp

# Windows: the LspServerWin MONOLITH (LSP modules + the whole
# EmperorPenguinLib source set) — the dyn-lib pair is ELF-specific
# (SONAME/$$ORIGIN/rpath/-rdynamic); file namespaces only use basenames so
# ../..-relative sources are safe. No meta (the Linux LSP carries no JIT
# either). Windows coroutines & stdio events come from the runtime's fiber
# scheduler + PeekNamedPipe fd integration (EmperorPenguin/std/c/scheduler.c).
build/win/MagellanicPenguinLSP.ll: $(BS)/pass4 $(LSPWIN_SRC) $(EP_STD)
	@mkdir -p $(@D) build/logs
	@echo "LSP (win): emitting build/win/MagellanicPenguinLSP.ll"
	@set -o pipefail; { \
	$(BS)/pass4 MagellanicPenguin/LspServer/LspServerWin.penguins \
	    --enable-coroutine -vv -o build/win/MagellanicPenguinLSP $(TEE) build/logs/lsp-win.log; \
	} || { echo "Windows LSP build FAILED at emission" >&2; exit 1; }

build/win/MagellanicPenguinLSP.exe: build/win/MagellanicPenguinLSP.ll $(C_RT)
	@mkdir -p $(@D) build/logs
	@set -o pipefail; { \
	OPT=-O2 EmperorPenguin/emperor link build/win/MagellanicPenguinLSP.ll \
	    -o build/win/MagellanicPenguinLSP.exe -target=win64 $(TEE) build/logs/lsp-win-link.log; \
	} || { echo "Windows LSP build FAILED at link" >&2; exit 1; }
ifeq ($(HOST),win)
	@# The shared Tests/LspTest path expects build/lsp (the md files name it
	@# in Run Args); the monolith is self-contained, no .penguin-lib needed.
	@cp -f build/win/MagellanicPenguinLSP.exe build/lsp
endif

lsp_win: build/win/MagellanicPenguinLSP.exe

lsp: lsp_$(HOST)
	@echo "LSP build complete ($(HOST))"

# ── test / baseline_test ─────────────────────────────────────────────
test:
	@echo "Running PenguinLang Tests ..."
	@echo "============================================================"
	@mkdir -p build/logs
	@set -o pipefail; { \
	dotnet run --configuration Release --project Tests/PenguinTestRunner.csproj -- $(TEST_ARGS) $(TEE) build/logs/test.log; \
	} || { echo "Tests FAILED (full log: build/logs/test.log)" >&2; exit 1; }

baseline_test:
	@echo "Running PenguinLang Tests (recording baseline) ..."
	@echo "============================================================"
	@mkdir -p build/logs
	@set -o pipefail; { \
	dotnet run --configuration Release --project Tests/PenguinTestRunner.csproj -- --baseline $(TEST_ARGS) $(TEE) build/logs/test.log; \
	} || { echo "Tests FAILED (full log: build/logs/test.log)" >&2; exit 1; }

# ── publish ──────────────────────────────────────────────────────────
# Builds every platform's release + LSP (win -> linux cross compiling is not
# supported, so a windows host publishes win only), deploys them into the
# vscode extension, runs smoke tests from a foreign cwd, then the dotnet
# self-contained publishes and the vsix package.
ifeq ($(HOST),linux)
  PUBLISH_TARGETS := release_linux release_win lsp_linux lsp_win
else
  PUBLISH_TARGETS := release_win lsp_win
endif

publish: $(PUBLISH_TARGETS)
	@echo "Publishing self-contained executables + VSCode extension ..."
	@echo "============================================================"
ifeq ($(findstring linux,$(PUBLISH_TARGETS)),linux)
	@# --- linux: emitter + driver script + LSP pair + stdlib (penguin for the
	@# compilers, c sources for the script's runtime make) ---
	@mkdir -p MagellanicPenguin/vscode/server/linux/EmperorPenguin/std
	@cp build/linux/emperor_penguin_llvmir_emitter MagellanicPenguin/vscode/server/linux/
	@cp build/linux/emperor MagellanicPenguin/vscode/server/linux/
	@cp build/lsp MagellanicPenguin/vscode/server/linux/MagellanicPenguinLSP
	@cp build/libemperorpenguin.penguin-lib MagellanicPenguin/vscode/server/linux/
	@cp -r EmperorPenguin/std/penguin MagellanicPenguin/vscode/server/linux/EmperorPenguin/std/penguin
	@cp -r EmperorPenguin/std/include MagellanicPenguin/vscode/server/linux/EmperorPenguin/std/include
	@cp -r EmperorPenguin/std/c MagellanicPenguin/vscode/server/linux/EmperorPenguin/std/c
	@rm -f MagellanicPenguin/vscode/server/linux/EmperorPenguin/std/c/*.o MagellanicPenguin/vscode/server/linux/EmperorPenguin/std/c/*.a
	@echo "Deployed linux emitter + emperor script + LSP + stdlib -> MagellanicPenguin/vscode/server/linux/"
	@# Smoke test the BUNDLED driver script from a foreign cwd (no repo): the
	@# script must locate the std tree beside itself, drive the emitter, build
	@# the C runtime and link — then the program must run.
	@SMOKE_DIR=$$(mktemp -d); \
	REPO_ROOT=$$PWD; \
	printf 'let world : string = "wor"+"ld";\ninitial { println("Hello, " + world + "!"); }\n' > "$$SMOKE_DIR/t.penguin"; \
	(cd "$$SMOKE_DIR" && "$$REPO_ROOT/MagellanicPenguin/vscode/server/linux/emperor" t.penguin -o t && ./t > out.bin 2> err.bin); \
	SMOKE_RC=$$?; \
	if [ $$SMOKE_RC -ne 0 ] || ! grep -q "Hello, world!" "$$SMOKE_DIR/out.bin" 2>/dev/null; then \
	    echo "Publish FAILED: bundled emperor script smoke test (exit=$$SMOKE_RC)" >&2; \
	    cat "$$SMOKE_DIR/err.bin" >&2; \
	    rm -rf "$$SMOKE_DIR"; \
	    exit 1; \
	fi; \
	rm -rf "$$SMOKE_DIR"; \
	echo "Bundled emperor script smoke test passed (compile + link + run from a foreign cwd)"; \
	# Smoke test the BUNDLED LSP from a foreign cwd (no repo): a full \
	# session must answer capabilities, survive opening a broken document \
	# (the parser-panic path exercises the sjlj try/catch landing pad — \
	# publishing an 'internal compiler error' diagnostic, not dying) and \
	# exit 0 — proving the exe-dir stdlib discovery AND the rpath \
	# lib load work in a deployed layout. \
	SMOKE_DIR=$$(mktemp -d); \
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
	echo "Bundled LSP smoke test passed (initialize + broken-doc survival + shutdown/exit from a foreign cwd)"
endif
ifeq ($(findstring win,$(PUBLISH_TARGETS)),win)
	@# --- windows: emitter + emperor.bat + LSP monolith + stdlib trees ---
	@mkdir -p MagellanicPenguin/vscode/server/windows/EmperorPenguin/std
	@cp build/win/emperor_penguin_llvmir_emitter.exe MagellanicPenguin/vscode/server/windows/
	@cp build/win/emperor.bat MagellanicPenguin/vscode/server/windows/
	@cp build/win/MagellanicPenguinLSP.exe MagellanicPenguin/vscode/server/windows/
	@cp -r EmperorPenguin/std/penguin MagellanicPenguin/vscode/server/windows/EmperorPenguin/std/penguin
	@cp -r EmperorPenguin/std/include MagellanicPenguin/vscode/server/windows/EmperorPenguin/std/include
	@cp -r EmperorPenguin/std/c MagellanicPenguin/vscode/server/windows/EmperorPenguin/std/c
	@rm -f MagellanicPenguin/vscode/server/windows/EmperorPenguin/std/c/*.o MagellanicPenguin/vscode/server/windows/EmperorPenguin/std/c/*.a
	@echo "Deployed windows emitter + emperor.bat + LSP + stdlib -> MagellanicPenguin/vscode/server/windows/"
	@# LSP smoke session (initialize, broken-document didOpen exercising the
	@# Windows sjlj landing pad, shutdown/exit) from a foreign cwd — under
	@# wine on a linux host (WINE=<path> or wine on PATH), natively on
	@# windows. The emitter run smoke is skipped on wine (the meta DLLs'
	@# CRT deps need an MSYS2 staging; the binary is link-validated).
	@SMOKE_DIR=$$(mktemp -d); \
	SERVER="$$PWD/MagellanicPenguin/vscode/server/windows/MagellanicPenguinLSP.exe"; \
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
	# Windows meta JIT runtime DLLs ride along in the vscode windows server dir \
	# (only the three LLVM DLLs are vendored in thirdparty/.../bin; their own \
	# deps — libc++/libffi/zlib/zstd/libxml2/winpthread — must be staged from \
	# the MSYS2 clang64 bin for the meta exe to actually run). \
	if [ -d "thirdparty/mingw-w64-x86_64-llvm-libs/bin" ]; then \
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
