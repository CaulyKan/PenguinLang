#!/usr/bin/env bash
# install-deps-ubuntu.sh — install the runtime dependencies of the released
# Linux EmperorPenguin binaries on Ubuntu 22.04+ (also Debian derivatives).
#
# The binaries are built on Arch, where LLVM 22 installs /usr/lib/libLLVM.so.22.1
# (package llvm-libs). Debian/Ubuntu ship the very same library under the name
# libLLVM-22.so.1 (package libllvm22), so besides installing the packages this
# script creates a compatibility symlink:
#
#     /usr/local/lib/libLLVM.so.22.1 -> <path>/libLLVM-22.so.1
#
# Everything else in the ldd closure (libstdc++, libedit, ncurses, zlib, zstd,
# libxml2, icu, libffi, ...) comes in transitively through the Ubuntu libLLVM
# package and matches Ubuntu's own libraries automatically.
#
# Base requirements: glibc >= 2.34 and GLIBCXX_3.4.30 — satisfied by the base
# system on Ubuntu 22.04+ (older releases will refuse to run the binaries).
#
# LLVM 22 comes from the distro repository when available (Ubuntu 26.04+),
# otherwise the apt.llvm.org repository is added automatically. Override the
# detected codename with LLVM_DIST_CODENAME=<codename> for derivatives that
# apt.llvm.org does not mirror (Mint, Pop!_OS, ...).
#
# Usage:
#   EmperorPenguin/scripts/install-deps-ubuntu.sh [--with-toolchain] [binary...]
#       --with-toolchain  also install make + clang-22 (the emperor driver
#                         needs them to build the C runtime and link programs;
#                         clang-22 matches the LLVM the emitter was built with)
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

command -v apt-get >/dev/null 2>&1 \
    || { echo "error: apt-get not found (this script targets Ubuntu/Debian)" >&2; exit 1; }

fetch() {  # fetch <url> — to stdout
    if command -v curl >/dev/null 2>&1; then curl -fsSL "$1"
    else wget -qO- "$1"
    fi
}

pkgs=(libllvm22)
if [ "$with_toolchain" -eq 1 ]; then pkgs+=(make clang-22); fi

$sudo apt-get update -qq

if apt-cache show libllvm22 >/dev/null 2>&1; then
    echo "[ubuntu] installing ${pkgs[*]} from the distro repository"
else
    dist=${LLVM_DIST_CODENAME:-}
    if [ -z "$dist" ]; then
        # shellcheck disable=SC1091
        . /etc/os-release
        dist=$VERSION_CODENAME
    fi
    echo "[ubuntu] libllvm22 not in the distro repo — adding apt.llvm.org (dist: $dist)"
    $sudo install -d /etc/apt/keyrings
    fetch https://apt.llvm.org/llvm-snapshot.gpg.key \
        | $sudo gpg --dearmor --yes -o /etc/apt/keyrings/apt-llvm.org.gpg
    echo "deb [signed-by=/etc/apt/keyrings/apt-llvm.org.gpg] http://apt.llvm.org/$dist llvm-toolchain-$dist-22 main" \
        | $sudo tee /etc/apt/sources.list.d/apt-llvm-org-llvm22.list >/dev/null
    $sudo apt-get update -qq
fi
$sudo apt-get install -y --no-install-recommends "${pkgs[@]}"

# Locate libLLVM-22.so.1 (ldconfig cache first, known paths as fallback).
llvm_lib=$(ldconfig -p 2>/dev/null | awk '/libLLVM-22\.so\.1 /{print $NF; exit}')
if [ -z "$llvm_lib" ]; then
    for c in /usr/lib/x86_64-linux-gnu/libLLVM-22.so.1 \
             /usr/lib/llvm-22/lib/libLLVM-22.so.1; do
        if [ -e "$c" ]; then llvm_lib=$c; break; fi
    done
fi
[ -n "$llvm_lib" ] || { echo "error: libLLVM-22.so.1 not found after install" >&2; exit 1; }

# Compatibility symlink: the binaries NEEDED the Arch-style soname.
link=/usr/local/lib/libLLVM.so.22.1
if [ -e "$link" ] || [ -L "$link" ]; then
    echo "[ubuntu] compatibility symlink already present: $link"
else
    $sudo ln -s "$llvm_lib" "$link"
    echo "[ubuntu] created $link -> $llvm_lib"
fi
$sudo ldconfig

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
