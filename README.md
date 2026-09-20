## Penguin Language

[English](README.md) | [简体中文](README.zh-CN.md)

Penguin language (or 'penguin-lang') is a programming language designed to be easy to use, easy to understand, and concurrency-friendly.

Here is a minimal hello-world example:
```
initial {
	println("hello world from penguin-lang!");
}
```

Documentation site: [https://caulykan.github.io/PenguinLang/](https://caulykan.github.io/PenguinLang/)

Penguin-lang draws design inspiration from multiple languages:
* Syntax from C
* Garbage collection from C#/Java
* Type system from Rust
* Async/coroutines from Golang
* Concurrency model from Verilog/SystemC

Family members:
* BabyPenguin: C# implementation of the Penguin-lang compiler & VM, mainly used for bootstrapping
* MagellanicPenguin: Penguin-lang Language Server Protocol (LSP) & Debug Adapter Protocol (DAP) implementation based on BabyPenguin
* EmperorPenguin: A Penguin-lang compiler written with BabyPenguin, emits LLVM IR, compiled into binaries

How to use:
* Download the latest Win/Linux packages from Release
* Install dependencies with the release package's `install-deps-ubuntu.sh` or the script for your OS
* Compile and run with BabyPenguin: `baby_penguin hello.penguin`
* Compile with EmperorPenguin (requires running separately): `emperor_penguin hello.penguin -o ./hello`

Build and run:
* Install dependencies with `EmperorPenguin/scripts/install-deps-ubuntu.sh --with-toolchain` or the script for your OS
* Build and produce the release package: `make release`
* Test: `make unittest test`
