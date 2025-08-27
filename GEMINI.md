# Project Overview

This project is the implementation of the "Penguin Language", a programming language designed to be easy-to-use, easy-to-understand, and concurrent-friendly. The project is primarily written in C# using the .NET SDK.

The project is divided into several sub-projects:

*   **BabyPenguin:** The core of the project, containing the compiler and virtual machine for the Penguin Language. It takes Penguin Language source files, compiles them into an intermediate representation, and runs them on a virtual machine.
*   **PenguinLangSyntax:** Contains the ANTLR grammar (`PenguinLang.g4`) and the syntax tree definitions for the Penguin Language.
*   **BabyPenguin.Tests:** Contains unit tests for the BabyPenguin compiler and VM.
*   **MagellanicPenguin:** Implements the Language Server Protocol (LSP) and Debug Adapter Protocol (DAP) for the Penguin Language, enabling IDE features like code completion, syntax highlighting, and debugging. This includes a VS Code extension.

# Building and Running

## Dependencies

*   .NET 8.0 SDK
*   Node.js and npm (for the VS Code extension)

## Key Commands

*   **Build the project:**
    ```bash
    dotnet build
    ```
*   **Run the tests:**
    ```bash
    dotnet test
    ```
*   **Run a Penguin Language file:**
    ```bash
    dotnet run --project .\BabyPenguin -- .\Examples\HelloWorld.penguin
    ```
*   **Package the VS Code extension:**
    ```bash
    cd MagellanicPenguin\vscode && npm install && npm run package
    ```
*   **Publish the project for Windows and Linux:**
    ```bash
    # For Windows
    dotnet publish -r win-x64 --self-contained

    # For Linux
    dotnet publish -r linux-x64 --self-contained
    ```

# Development Conventions

*   The project follows standard C# coding conventions.
*   The language grammar is defined using ANTLR in `PenguinLangSyntax/PenguinLang.g4`.
*   The VS Code extension is developed using TypeScript and packaged with `npm`.
*   The project uses a solution file (`.sln`) to manage the C# projects.
