# MagellanicPenguin - Penguin Language Support for Visual Studio Code

Welcome to MagellanicPenguin, the official VS Code extension for the Penguin programming language.

## What is Penguin Language?

Penguin is a programming language designed to be easy to use, easy to understand, and concurrency-friendly. It simplifies the development of complex applications through an event-driven and asynchronous model. This project is the C# implementation of the language, which includes a virtual machine (`BabyPenguin`) and a language server (`MagellanicPenguin`).

## Features

*   **Syntax Highlighting**: Provides code highlighting for `.penguin` files.
*   **Debugging Support**: Debug your Penguin programs directly within VS Code.
*   **Code Snippets**: (Planned) Quickly insert common code structures.
*   **Code Completion & Diagnostics**: (Planned) Provides intelligent code completion and real-time error checking.

## Quick Start

Follow the steps below to start writing and running your first Penguin program.

### 1. Write Your First Program

Create a file named `HelloWorld.penguin` and add the following code:

```penguin
initial {
    println("Hello, World!");
}
```

### 2. Configure the Debugger

To run and debug Penguin code in VS Code, you need to configure the `launch.json` file.

1.  Create a `.vscode` folder in your project's root directory (if it doesn't already exist).
2.  Create a file named `launch.json` inside that folder.
3.  Paste the following configuration into `launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "PenguinLang Debug",
            "type": "penguinlang",
            "request": "launch",
            "program": "${file}",
        }
    ]
}
```


### 3. Run the Program

1.  Open the `HelloWorld.penguin` file you want to run.
2.  Go to the "Run and Debug" view (Ctrl+Shift+D).
3.  Select the "Run Penguin File" configuration from the dropdown at the top.
4.  Click the green play button or press `F5`.

You should see the "Hello, World!" output in the VS Code integrated terminal.