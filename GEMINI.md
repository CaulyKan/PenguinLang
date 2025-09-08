# 项目总览

本项目是 “Penguin Language” 的实现，这是一种旨在易于使用、易于理解且对并发友好的编程语言。项目主要使用 C# 和 .NET SDK 编写。

项目分为几个子项目：

*   **BabyPenguin:** 项目的核心，包含 Penguin Language 的编译器和虚拟机。它接收 Penguin Language 源文件，将其编译为中间表示，并在虚拟机上运行。
*   **PenguinLangParser:** 包含 ANTLR 语法 (`PenguinLang.g4`) 和 Penguin Language 的语法树定义。
*   **BabyPenguin.Tests:** 包含 BabyPenguin 编译器和虚拟机的单元测试。
*   **MagellanicPenguin:** 为 Penguin Language 实现语言服务器协议 (LSP) 和调试适配器协议 (DAP)，以支持代码补全、语法高亮和调试等 IDE 功能。该项目包含一个 VS Code 插件。

# 当前工作目标：在 BabyPenguin 上构建 EmperorPenguin

### 1. 目标

目标是创建一个名为 `EmperorPenguin` 的编译器，该编译器使用 PenguinLang 编写，可将 PenguinLang 源代码翻译成 LLVM IR。该编译器将在 `BabyPenguin` 虚拟机上运行。真正的自举并非短期目标。

### 2. 编译器架构：“序列化AST”方案

为了避免 FFI 的复杂性，并创建一个清晰、可调试的架构，我们将采用一个基于文件的、分多阶段的编译过程。该过程将解析与编译彻底解耦。

1.  **AST解析器 (C# 工具):** 我们将改造现有的 `PenguinLangParser` 库项目，使其成为一个可以生成序列化AST的命令行工具。
2.  **`EmperorPenguin` (PenguinLang 编译器):** `EmperorPenguin` 是主编译器，它使用 PenguinLang 编写并运行在 `BabyPenguin` 之上。它不会直接解析源代码，而是读取序列化的AST文件，解析其中的 S-表达式，并在内存中重建 AST。然后，它将对重建的 AST 进行语义分析，并据此生成 LLVM IR。
3.  **LLVM工具** 使用llvm工具链将ll文件编译为可执行文件。

该方案提供了清晰的关注点分离，并为未来的自举提供了一条更稳健的路径。

### 3. S-表达式方案选型理由

我们选择 S-表达式作为中间 AST 格式，而不是 JSON 或自定义二进制格式，原因如下：
*   **解析简单**: S-表达式拥有非常简单和规整的语法 (`(item item ...)`), 这使得我们需要在 PenguinLang 中编写的解析器比一个完整的 JSON 解析器要简单得多。
*   **人类可读**: 该格式基于文本且易于阅读，这对于调试 AST 解析器的输出非常有价值。这是相比自定义二进制格式的一个主要优势。
*   **良好的平衡点**: 它在易于实现和易于调试之间取得了最佳的平衡。

### 4. 分阶段开发计划

**阶段一: 解析器工具 (使用 C#)**

*   [x] **任务 1: 将 `PenguinLangParser` 改造为可执行程序**
    *   **理由**: 此方案避免了为解决方案增加一个新项目，让项目结构更清晰，且逻辑内聚性好（AST的定义、解析器、序列化工具都在一起）。
    *   [x] 将 `PenguinLangParser.csproj` 的 `<OutputType>` 从 `Library` 修改为 `Exe`。
    *   [x] 在 `PenguinLangParser` 项目中添加 `Program.cs` 文件及 `Main` 入口函数。
    *   [x] 在 `Main` 函数中实现命令行参数处理 (`--input`, `--output`)。
    *   [x]  将项目名修改为 `PenguinLangParser`，使其生成 `PenguinLangParser.exe`。
    *   [x] 在 `Main` 函数中调用 `SyntaxCompiler` 解析输入文件，并实现将 AST 序列化为 S-表达式的逻辑。
    *   [x] 做一个测试用例，确保解析器正确地生成 S-表达式。

**阶段二: 基础标准库 (使用 C#)**

*   [ ] **任务 2: 增强 `BabyPenguin` 标准库**
    *   **理由**: `EmperorPenguin` 编译器需要一个功能强大的标准库才能运行。
    *   **计划**: 在 `BabyPenguin/ExternFunctions.cs` 中实现以下函数和类型，并作为 `extern` 暴露给外部：
        *   [ ] `File.read_text(path: string) -> string`
        *   [ ] `File.write_text(path: string, content: string)`
        *   [ ] `Map<K, V>` (由 `System.Collections.Generic.Dictionary` 支持)
        *   [ ] `StringBuilder`
        *   [ ] `Args.get() -> string[]`
        *   [ ] `eprintln(text: string)`

**阶段三: 编译器 (使用 PenguinLang)**

*   [ ] **任务 3: 实现 `EmperorPenguin`**
    *   [ ] 创建 `EmperorPenguin` 项目目录和 `.penguins` 项目文件。
    *   [ ] **`sexp_parser.penguin`**: 在 PenguinLang 中实现一个能够解析 S-表达式格式的解析器。
    *   [ ] **`ast_builder.penguin`**: 接收解析后的 S-表达式数据，并在内存中重建 PenguinLang AST。
    *   [ ] **`semantic.penguin`**: 对重建的 AST 进行语义分析和类型检查。
    *   [ ] **`ir_generator.penguin`**: 遍历检查后的 AST，并构建 LLVM IR 字符串。
    *   [ ] **`main.penguin`**: 协调以上步骤的入口点。

### 5. 最终工作流

端到端的过程将是一个两步命令：

```bash
# 第一步: 将源代码解析为序列化的 AST
PenguinParser.exe --input my_app.penguin --output my_app.ast.sexp

# 第二步: 将 AST 编译为 LLVM IR
dotnet run --project BabyPenguin -- EmperorPenguin/src/main.penguin --input my_app.ast.sexp --output my_app.ll

# 第三部：将 LLVM IR 编译为可执行文件
clang -o my_app.exe my_app.ll
```

### 6. 运行时系统设计考量

虽然初期的重点是编译器的实现，但 `EmperorPenguin` 的一个关键任务是生成能在定义良好的**运行时系统 (Runtime System)** 中运行的代码。与依赖 .NET 运行时的 `BabyPenguin` 不同，`EmperorPenguin` 必须为原生可执行文件定义 PenguinLang 的特性在内存中如何表示。

这是一个重要的工作领域，将与编译器并行开发。其高层设计原则将包括：

*   **通用对象头**: 每个堆分配的对象都会有一个通用的头部，包含运行时必需的元数据，如指向其类型信息的指针和垃圾回收的标记位。
*   **类型信息结构**: 对于每种类型，都会有一个静态的全局结构，包含其大小、接口实现表 (V-Tables) 等类型特定的元数据。
*   **内存布局**: 为类、字符串和其他引用类型在内存中的布局制定清晰的约定。
*   **接口表示**: 接口将表示为“胖指针”。
*   **垃圾回收 (GC)**: 运行时需要一个垃圾回收器。

# 构建与运行

## 依赖

*   .NET 8.0 SDK
*   Node.js 和 npm (用于 VS Code 插件)

## 关键命令

*   **构建项目:**
    ```bash
    dotnet build
    ```
*   **运行测试:**
    ```bash
    dotnet test
    ```
    注意因为测试用例较多，我们应该优先考虑有目标的运行单独的测试用例


# 开发约定

*   项目遵循标准的 C# 编码约定。
*   语言语法在 `PenguinLangParser/PenguinLang.g4` 中使用 ANTLR 定义。
*   VS Code 插件使用 TypeScript 开发，并使用 `npm` 打包。
*   项目使用解决方案文件 (`.sln`) 来管理 C# 项目。
*   Documentation中包含关于Penguinlang的文档