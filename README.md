## Penguin Language

[English](README.md) | [简体中文](README.zh-CN.md)

Penguin language (or 'penguin-lang') is designed to be a programming language that is easy-to-use, easy-to-understand, and concurrent-friendly. 

Following is a minimal hello-world examle:
```
initial {
	println("hello world from penguin-lang!");
}
```

Penguin-lang is inspired from multiple languages:
* Syntax from C
* Garbage collect from C#/Java
* Type system from Rust
* Asynchronization or co-routines from Golang
* Concurrent from Verilog/SystemC

Roadmap:
* BabyPenguin: A C# implementation of Penguin-lang compiler & VM, emit BabyPenguinIR (Working with limited features)
* MagellanicPenguin: Language Server Protocol & Debug Adapter Protocol implementation for Penguin-lang based on BabyPenguin (Working with limited features)
* EmperorPenguin: A BabyPenguin implementation of Penguin-lang compiler, emits llvm IR (Not started)

Get Started:
* [Basic Introduction (Tutorial)](./docs/en/tutorials/BasicIntroduction.md)
* [Compiler Usage (build, bootstrap, run)](./docs/en/tutorials/CompilerUsage.md)
* [Types and Templates](./docs/en/tutorials/TypesAndTemplates.md)
* [Metaprogramming](./docs/en/tutorials/MetaProgramming.md)
* [Async, Timing and Modules](./docs/en/tutorials/AsyncTimingAndModularity.md)
* Online documentation site: [https://caulykan.github.io/PenguinLang/](https://caulykan.github.io/PenguinLang/) (built from [docs/](./docs) in this repo, English + 简体中文 with a language switcher)
* [Overview](./docs/en/specifications/01_Overview.md)
* [Basic Execution Flow](./docs/en/specifications/02_BasicExecutionFlow.md)
* [Data Types](./docs/en/specifications/03_DataTypes.md)
* [Function](./docs/en/specifications/04_Function.md)
* [Class](./docs/en/specifications/05_Class.md)
* [Enum](./docs/en/specifications/06_Enum.md)
* [Interface](./docs/en/specifications/07_Interface.md)
* [Namespace & Project](./docs/en/specifications/08_NamespaceAndProject.md)
* [Async & Timing Model](./docs/en/specifications/09_AsyncAndTimingModel.md)
* [Modular Programming](./docs/en/specifications/10_ModularProgramming.md)
* [Meta Programming](./docs/en/specifications/11_MetaProgramming.md)
* Implementation notes (compiler internals): [docs/en/impl-notes](./docs/en/impl-notes)

Build And Run:
* Install the .NET 8.0 SDK from https://dotnet.microsoft.com/download
* dotnet build
* dotnet test
* dotnet run --project .\BabyPenguin -- .\Examples\HelloWorld.penguin

Build the native self-hosting compiler & tooling with the root Makefile (all artifacts land in `build/`):
* make bootstrap — self-bootstrap EmperorPenguin (build/bootstrap/pass2..pass4 + convergence check; host only)
* make release — native .ll emitter + emperor driver script (make release_linux / make release_win per platform; cross compiling is linux→win only, handled by EmperorPenguin/emperor_penguin, and everything also works natively on Windows)
* make release_babypenguin — the C# compiler as a self-contained single-file binary (make release_babypenguin_linux → build/linux/baby_penguin, make release_babypenguin_win → build/win/baby_penguin.exe)
* make lsp — the PenguinLang-native LSP server (make lsp_linux / make lsp_win per platform)
* make test — the cross-compiler markdown test suite (make baseline_test records a new baseline)
* make publish — release artifacts: emitters, LSP servers, baby_penguin single-file binaries, VSCode extension package (both platforms on a linux host, win only on Windows)
* make all — bootstrap + lsp + test
