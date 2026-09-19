## Penguin 语言

[English](README.md) | [简体中文](README.zh-CN.md)

Penguin 语言（或称 'penguin-lang'）是一门易用、易理解、面向并发友好的编程语言。

下面是一个最小的 hello-world 示例：
```
initial {
	println("hello world from penguin-lang!");
}
```

Penguin-lang 的设计灵感来自多种语言：
* 语法来自 C
* 垃圾回收来自 C#/Java
* 类型系统来自 Rust
* 异步/协程来自 Golang
* 并发模型来自 Verilog/SystemC

路线图：
* BabyPenguin：Penguin-lang 编译器与虚拟机的 C# 实现，输出 BabyPenguinIR（开发中，功能有限）
* MagellanicPenguin：基于 BabyPenguin 的 Penguin-lang 语言服务器协议（LSP）与调试适配器协议（DAP）实现（开发中，功能有限）
* EmperorPenguin：用 BabyPenguin 编写的 Penguin-lang 编译器，输出 LLVM IR（尚未开始）

入门：
* [基础入门（教程）](./docs/zh/tutorials/BasicIntroduction.md)
* [编译器使用（构建、自举、运行）](./docs/zh/tutorials/CompilerUsage.md)
* [类型与模板](./docs/zh/tutorials/TypesAndTemplates.md)
* [元编程](./docs/zh/tutorials/MetaProgramming.md)
* [异步、时序与模块](./docs/zh/tutorials/AsyncTimingAndModularity.md)
* 在线文档站点：https://caulykan.github.io/PenguinLang/ （由本仓库的 [docs/](./docs) 构建，中文 + English 可通过语言切换按钮互相切换）
* [概述](./docs/zh/specifications/01_Overview.md)
* [基本执行流](./docs/zh/specifications/02_BasicExecutionFlow.md)
* [数据类型](./docs/zh/specifications/03_DataTypes.md)
* [函数](./docs/zh/specifications/04_Function.md)
* [Class](./docs/zh/specifications/05_Class.md)
* [枚举](./docs/zh/specifications/06_Enum.md)
* [接口](./docs/zh/specifications/07_Interface.md)
* [命名空间与工程](./docs/zh/specifications/08_NamespaceAndProject.md)
* [异步与时序模型](./docs/zh/specifications/09_AsyncAndTimingModel.md)
* [模块化编程](./docs/zh/specifications/10_ModularProgramming.md)
* [元编程](./docs/zh/specifications/11_MetaProgramming.md)
* 实现笔记（编译器内部实现）：[docs/zh/impl-notes](./docs/zh/impl-notes)

构建与运行：
* 从 https://dotnet.microsoft.com/download 安装 .NET 8.0 SDK
* dotnet build
* dotnet test
* dotnet run --project .\BabyPenguin -- .\Examples\HelloWorld.penguin

使用根目录 Makefile 构建原生自举编译器与工具链（所有产物位于 `build/` 目录）：
* make bootstrap — EmperorPenguin 自举（构建 build/bootstrap/pass2..pass4 + 收敛性检查；仅宿主平台）
* make release — 原生 .ll 发射器 + emperor 驱动脚本（按平台执行 make release_linux / make release_win；交叉编译仅支持 linux→win，由 EmperorPenguin/emperor 处理，Windows 宿主上全部原生构建）
* make lsp — PenguinLang 原生（自举）实现的 LSP 服务器（按平台执行 make lsp_linux / make lsp_win）
* make test — 跨编译器 markdown 测试套件（make baseline_test 记录新基线）
* make publish — 发布产物：发射器、LSP 服务器、dotnet 自包含可执行文件、VSCode 扩展包（linux 宿主上构建双平台，Windows 宿主上仅 win）
* make all — bootstrap + lsp + test
