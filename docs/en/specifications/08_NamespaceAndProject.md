# Namespace & Project

This chapter defines symbol organization: namespaces, `using`, extern functions, the `export` marker, source files, and `.penguins` project files. Building and consuming shared libraries is covered in [Modular Programming](./10_ModularProgramming.md) §Libraries.

## Namespace
PenguinLang uses namespaces to avoid naming conflicts. The concept is similar to C# namespaces. Each file has a default per-file anonymous namespace for code that is not written inside a `namespace` declaration.

```
let a = 0;   // full name: _ns_<file>.a  (per-file anonymous namespace)

namespace MyModule {
	let b = 0;   // full name: MyModule.b
}

initial {
	MyModule.b = 1;   // qualified reference
}
```

Notes:
- Top-level definitions (not inside any `namespace` block) live in a per-file anonymous namespace (C++ `static` semantics): visible unqualified inside their own file, requiring qualification from other files.
- The `__builtin` namespace is always implicitly used — its symbols (`Option`, `panic`, string builtins, …) resolve unqualified everywhere.

## Nested Namespaces
Namespaces can be nested; member access chains through namespace symbols at every depth:
```
namespace std {
	namespace io {
		fun read_all() -> string {
			return std.io.stdin_read_all();
		}
	}
}

initial {
	let s : string = std.io.read_all();
}
```
Nested-namespace member access (`std.io.x()`) is implemented in the EmperorPenguin front-end; the BabyPenguin (C# reference) compiler does not resolve it.

## using
The `using` statement imports a namespace by name, similar to C#:
```
using MyModule;
initial {
	b = 1;   // implicit reference to MyModule.b
	MyModule.b = 1;   // explicit reference to MyModule.b
}
```
`using <ns>;` is accepted at file top level and inside namespace bodies. It is implemented in the EmperorPenguin front-end; the BabyPenguin (C# reference) grammar does not parse `using`.

## Extern Functions
`extern fun` declares a function implemented by the C runtime (or the host VM). The symbol mapping follows a universal rule: an extern declared inside any namespace maps to the C symbol `@<full.dotted.name>` with `.` replaced by `_`; a bare top-level extern keeps its literal libc symbol:
```
namespace std { namespace io {
	extern fun file_open(path: string, mode: string) -> u64;   // -> @std_io_file_open
} }

extern fun abs(x: double) -> double;   // -> @abs (libc)
```
An extern declared inside a namespace must be called qualified (`std.io.file_open(...)`); the call-site spelling is preserved in the IR. Unreferenced extern declarations are not emitted.

## export
The `export` prefix marks a top-level definition as part of a library's public interface when the file is built into a `.penguin-lib`:

```
export fun pick(a: i32, b: i32) -> i32 { return a; }

export class Widget {
    size: i32;
    impl IReferenceType;
}

export namespace Toolkit {
    fun helper() -> i32 { return 1; }
}
```

Rules:

* `export` is accepted before any top-level declaration — `fun`, `class`, `enum`, `interface`, `namespace`, `type`, `let` — including before a `#template(...)` prefix (`export #template(T: type) class Vector { ... }`; the EmperorPenguin front-end also accepts `export` between `#template(...)` and the definition).
* `export namespace` cascades to every nested definition inside the namespace.
* A definition's `export` marking is consumed **only when building a library**: export-marked definitions (plus the types their signatures reference, every global variable, every top-level `impl X for Y` edge, and verbatim source for files containing template/meta constructs) enter the library's metadata; everything else stays private to the compiled `.so`. Normal (non-lib) compiles ignore `export`.

See [Modular Programming](./10_ModularProgramming.md) §Libraries for the full keep-set rules.

## Source File
PenguinLang uses `.penguin` as the source file extension. PenguinLang does not enforce any restriction on files and directories.

## Project
PenguinLang supports single file compilation, but for larger software a project file is necessary. The project file uses the `.penguins` extension and an INI-style format: a `[Project]` section with `key="value"` pairs; blank lines and `#` comments are ignored.
```
[Project]
# single-quoted-array style: sources, libs and flags are string arrays
name="MyPenguin"
sources=[
	"a.penguin",
	"b.penguin",
	"src/**/*.penguin"
]
libs=["../shared/libfoo.penguin-lib"]
flags=["-enable-coroutine"]
```
Keys:
| key | value | meaning |
| --- | --- | --- |
| `name` | quoted string | project name |
| `sources` | string array | source files / glob patterns (`*`, `**`, `?`) resolved relative to the project file |
| `lib` / `libs` | string array | shared libraries to compile against (`.penguin-lib`; see [Modular Programming](./10_ModularProgramming.md)) |
| `flags` | string array | compiler option tokens (entries not starting with `-` are ignored); project flags are applied as if given on the command line |
| `meta-sources` | string array | extra source files visible to compile-time `#fun` code (see [Meta Programming](./11_MetaProgramming.md)) |

A project file can never inject source files beyond `sources`; relative `libs` paths resolve against the project directory. Passing a `.penguins` file to the compiler instead of a `.penguin` file compiles the whole project.
