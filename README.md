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
* BabyPenguin: A C# implementation of Penguin-lang compiler & VM, emit BabyPenguinIR (Working)
* MagellanicPenguin: Language Server Protocol & Debug Adapter Protocol implementation for Penguin-lang based on BabyPenguin (DAP Working, LSP not started)
* EmperorPenguin: A BabyPenguin implementation of Penguin-lang compiler, emits llvm IR (Not started)

Documents:
* [Execution Flow And Events](./Documentation/02_ExecutionFlowAndEvents.md)
* [Data Types](./Documentation/03_DataTypes.md)
* [Function](./Documentation/04_Function.md)
* [Enum](./Documentation/05_Enum.md)
* [Interface](./Documentation/06_Interface.md)
* [Asynchronization And Concurrency](./Documentation/07_AsynchronizationAndConcurrency.md)
* [Timing Model](./Documentation/08_TimingModel.md)
* [Namespace And Project](./Documentation/09_NamespaceAndProject.md)

Build And Run:
* Install the .NET 8.0 SDK from https://dotnet.microsoft.com/download
* dotnet build
* dotnet test
* dotnet run --project .\BabyPenguin -- .\Examples\HelloWorld.penguin
