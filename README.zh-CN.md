## Penguin 语言

[English](README.md) | [简体中文](README.zh-CN.md)

Penguin 语言（或称 'penguin-lang'）是一门易用、易理解、面向并发友好的编程语言。

下面是一个最小的 hello-world 示例：
```
initial {
	println("hello world from penguin-lang!");
}
```

文档站点：[https://caulykan.github.io/PenguinLang/](https://caulykan.github.io/PenguinLang/)

Penguin-lang 的设计灵感来自多种语言：
* 语法来自 C
* 垃圾回收来自 C#/Java
* 类型系统来自 Rust
* 异步/协程来自 Golang
* 并发模型来自 Verilog/SystemC

家族成员：
* BabyPenguin：Penguin-lang 编译器与虚拟机的 C# 实现，主要用于自举
* MagellanicPenguin：基于 BabyPenguin 的 Penguin-lang 语言服务器协议（LSP）与调试适配器协议（DAP）实现
* EmperorPenguin：用 BabyPenguin 编写的 Penguin-lang 编译器，输出 LLVM IR，编译为二进制文件

如何使用：
* 在Release下载最新的Win/Linux包
* 使用release包的`install-deps-ubuntu.sh`或其他系统的脚本安装依赖
* 使用BabyPenguin进行编译和运行：`baby_penguin hello.penguin`
* 使用EmperorPenguin进行编译（需要单独运行）：`emperor_penguin hello.penguin -o ./hello`

构建与运行：
* 使用`EmperorPenguin/scripts/install-deps-ubuntu.sh --with-toolchain`或其他系统的脚本安装依赖
* 编译并产生release包：`make release`
* 测试：`make unittest test`