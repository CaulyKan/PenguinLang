## Namespace
PenguinLang uses namespaces to avoid naming conflicts. The concept is similar to C# namespaces. PenguinLang supports a default global namespace for each file if code is not written in a namespace declaration.

PenguinLang also supports the `using` statement similar to C#.

```
let a = 0;   // full name: _ns_<file>.a  (per-file anonymous namespace)

namespace MyModule {
	let b = 0;   // full name: MyModule.b
}

using MyModule;
initial {
	b = 1;   // implicit referencing MyModule.b
	MyModule.b = 1;   // explicit referencing MyModule.b
}

```

Notes:
- Top-level definitions (not inside any `namespace` block) live in a per-file anonymous namespace (C++ `static` semantics): unqualified-visible inside their own file, requiring qualification from other files.
- `using <ns>;` is accepted at file top level and inside namespace bodies. The `__builtin` namespace is always implicitly used (its symbols — `Option`, `panic`, string builtins, … — resolve unqualified everywhere).
- Status: implemented in EmperorPenguin (lexer/parser/semantic). The BabyPenguin (C# reference) grammar does not parse `using` yet.

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