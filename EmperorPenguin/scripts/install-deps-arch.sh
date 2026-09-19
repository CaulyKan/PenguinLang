#!/usr/bin/env bash
# install-deps-arch.sh — install the runtime dependencies of the released
# Linux EmperorPenguin binaries on Arch Linux / CachyOS (pacman).
#
# ldd(emperor_penguin_llvm_emitter) closure and the packages providing it:
#   libLLVM.so.22.1        -> llvm-libs
#   libstdc++.so.6         -> gcc-libs        libgcc_s.so.1 -> gcc-libs
#   libc.so.6 / libm / ld-linux -> glibc
#   libffi.so.8            -> libffi          (dep of libLLVM)
#   libedit.so.0           -> libedit         (dep of libLLVM)
#   libncursesw.so.6       -> ncurses         (dep of libedit)
#   libz.so.1              -> zlib            (dep of libLLVM)
#   libzstd.so.1           -> zstd            (dep of libLLVM)
#   libxml2.so.16          -> libxml2         (dep of libLLVM)
#   libicuuc/icudata.so.78 -> icu             (dep of libxml2)
#
# NOTE llvm-libs must stay 22.1.x — the binaries hard-require the soname
# libLLVM.so.22.1. When Arch moves to the next LLVM major, re-run
# `make release` on the new toolchain.
#
# Usage:
#   EmperorPenguin/scripts/install-deps-arch.sh [--with-toolchain] [binary...]
#       --with-toolchain  also install clang + make (the emperor driver needs
#                         them to build the C runtime and link programs)
#       binary...         binaries to ldd-verify afterwards
#                         (default: build/linux/emperor_penguin_llvm_emitter)

set -euo pipefail

if [ "$(id -u)" -eq 0 ]; then sudo=""; else sudo="sudo"; fi

with_toolchain=0
bins=()
for a in "$@"; do
    case "$a" in
        --with-toolchain) with_toolchain=1 ;;
        -h|--help) grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) bins+=("$a") ;;
    esac
done
if [ ${#bins[@]} -eq 0 ]; then bins=(build/linux/emperor_penguin_llvm_emitter); fi

command -v pacman >/dev/null 2>&1 \
    || { echo "error: pacman not found (this script targets Arch Linux / CachyOS)" >&2; exit 1; }

pkgs=(glibc gcc-libs llvm-libs libffi libedit ncurses zlib zstd libxml2 icu)
if [ "$with_toolchain" -eq 1 ]; then pkgs+=(clang make); fi

echo "[arch] installing: ${pkgs[*]}"
$sudo pacman -S --needed -- "${pkgs[@]}"

rc=0
for b in "${bins[@]}"; do
    if [ ! -x "$b" ]; then
        echo "[verify] skip (not found): $b"
        continue
    fi
    missing=$(ldd "$b" 2>/dev/null | awk '/not found/{print $1}')
    if [ -n "$missing" ]; then
        echo "[verify] FAIL: $b"
        printf '    missing %s\n' $missing
        rc=1
    else
        echo "[verify] OK: $b"
    fi
done
exit $rc
