## Namespace
PenguinLang uses namespaces to avoid naming conflicts. The concept is similar to C# namespaces. PenguinLang supports a default global namespace for each file if code is not written in a namespace declaration.

PenguinLang also supports the `using` statement similar to C#.

```
var a = 0;   // full name: _global_xxx.a

namespace MyModule {
	var b = 0;   // full name: MyModule.b
}

using MyModule;
initial {
	b = 1;   // implicit referencing MyModule.b
	MyModule.b = 1;   // explicit referencing MyModule.b
}

```

## Source File
PenguinLang recommends using `.penguin` as the source file extension. PenguinLang does not enforce any restriction on files and directories. 

## Project
PenguinLang supports single file compilation, but for larger software a project file is necessary. The project file is recommended to use `.penguins` as the extension, and use TOML as the file format.
```
[project]
name = "MyPenguin"
sources = [
	"a.penguin",
	"b.penguin",
	"src/**/*.penguin"
]
```