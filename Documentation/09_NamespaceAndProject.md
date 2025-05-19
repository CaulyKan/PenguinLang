## Namespace
PenguinLang use namespaces to avoid naming conflict. The concept is very much similar to C# namespaces. Also, PenguinLang supports a default global namespace for each file if code is not written in namespace declartion.

PenguinLang also supports `using` grammar similar to C#.

```
val a = 0;	// full name: _global.a

namespace MyModule {
	var b = 0;   // full name: MyModule.b
}

using MyModule;  
initial {
	b = 1;	// implicit referencing MyModule.b
	MyModule.b = 1;	// explicit referencing MyModule.b
}

```

## Source File
PenguinLang recommands using `.penguin` as source file extension. PenguinLang do not enforce any restriction on files and directories. 

## Project
PenguinLang supports single file compilation, however for larger software a project file is necessary. The project file is recommanded to use `.penguins` as extension, and use toml as file format.
```
[project]
name = "MyPenguin"
sources = [
	"a.penguin",
	"b.penguin",
	"src/**/*.penguin"
]
```