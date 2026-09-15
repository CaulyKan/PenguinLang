## Penguin Language
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
* [Basic Introduction (Tutorial)](./docs/tutorials/BasicIntroduction.md)
* [Meta Programming Tutorial (EmperorPenguin)](./docs/tutorials/MetaProgrammingIntroduction.md)

Documentation:
* Online documentation site: https://caulykan.github.io/PenguinLang/ (built from [docs/](./docs) in this repo)
* [Overview](./docs/specifications/01_Overview.md)
* [Execution Flow And Events](./docs/specifications/02_ExecutionFlowAndEvents.md)
* [Data Types](./docs/specifications/03_DataTypes.md)
* [Function](./docs/specifications/04_Function.md)
* [Enum](./docs/specifications/05_Enum.md)
* [Interface](./docs/specifications/06_Interface.md)
* [Asynchronization And Concurrency](./docs/specifications/07_AsynchronizationAndConcurrency.md)
* [Timing Model](./docs/specifications/08_TimingModel.md)
* [Namespace And Project](./docs/specifications/09_NamespaceAndProject.md)
* [Meta Programming](./docs/specifications/10_MetaProgramming.md)
* [Ports Channels Events](./docs/specifications/11_PortsChannelsEvents.md)
* Implementation notes (compiler internals): [docs/impl-notes](./docs/impl-notes)

Build And Run:
* Install the .NET 8.0 SDK from https://dotnet.microsoft.com/download
* dotnet build
* dotnet test
* dotnet run --project .\BabyPenguin -- .\Examples\HelloWorld.penguin

Build the native self-hosting compiler & tooling with the root Makefile (all artifacts land in `build/`):
* make bootstrap — self-bootstrap EmperorPenguin (build/bootstrap/pass2..pass4 + convergence check; host only)
* make release — native .ll emitter + emperor driver script (make release_linux / make release_win per platform; cross compiling is linux→win only, handled by EmperorPenguin/emperor, and everything also works natively on Windows)
* make lsp — the PenguinLang-native LSP server (make lsp_linux / make lsp_win per platform)
* make test — the cross-compiler markdown test suite (make baseline_test records a new baseline)
* make publish — release artifacts: emitters, LSP servers, self-contained dotnet executables, VSCode extension package (both platforms on a linux host, win only on Windows)
* make all — bootstrap + lsp + test
