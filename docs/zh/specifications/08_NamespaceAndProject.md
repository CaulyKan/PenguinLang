# 命名空间与工程

本章定义符号组织：命名空间、`using`、extern 函数、`export` 标记、源文件与 `.penguins` 工程文件。共享库的构建与消费见[模块化编程](./10_ModularProgramming.md)的库一节。

## 命名空间
PenguinLang 用命名空间避免命名冲突，概念类似 C# 命名空间。每个文件有一个默认的按文件匿名命名空间，收纳未写在 `namespace` 声明内的代码。

```
let a = 0;   // 全名：_ns_<file>.a（按文件匿名命名空间）

namespace MyModule {
	let b = 0;   // 全名：MyModule.b
}

initial {
	MyModule.b = 1;   // 限定引用
}
```

说明：
- 顶层定义（不在任何 `namespace` 块内）位于按文件匿名命名空间中（C++ `static` 语义）：自己文件内可见无限定名，其他文件需要限定。
- `__builtin` 命名空间永远被隐式 using——其符号（`Option`、`panic`、字符串内建……）在任何位置无限定解析。

## 嵌套命名空间
命名空间可嵌套；成员访问链在每一深度经命名空间符号穿透：
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
嵌套命名空间成员访问（`std.io.x()`）由 EmperorPenguin 前端实现；BabyPenguin（C# 参考）编译器不解析它。

## using
`using` 语句按名导入命名空间，类似 C#：
```
using MyModule;
initial {
	b = 1;   // 对 MyModule.b 的隐式引用
	MyModule.b = 1;   // 对 MyModule.b 的显式引用
}
```
`using <ns>;` 在文件顶层与命名空间体内均可。由 EmperorPenguin 前端实现；BabyPenguin（C# 参考）语法不解析 `using`。

## extern 函数
`extern fun` 声明由 C 运行时（或宿主 VM）实现的函数。符号映射遵循一条通用规则：任何命名空间内声明的 extern 映射到 C 符号 `@<完整点分名>`（`.` 换成 `_`）；顶层裸 extern 保留字面 libc 符号：
```
namespace std { namespace io {
	extern fun file_open(path: string, mode: string) -> u64;   // -> @std_io_file_open
} }

extern fun abs(x: double) -> double;   // -> @abs（libc）
```
命名空间内声明的 extern 必须限定调用（`std.io.file_open(...)`）；IR 保留调用点拼写。未被引用的 extern 声明不会被输出。

## export
`export` 前缀把顶层定义标记为库的公共接口（当文件被构建为 `.penguin-lib` 时生效）：

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

规则：

* `export` 可置于任何顶层声明之前——`fun`、`class`、`enum`、`interface`、`namespace`、`type`、`let`——包括 `#template(...)` 前缀之前（`export #template(T: type) class Vector { ... }`；EmperorPenguin 前端也接受 `#template(...)` 与定义之间的 `export`）。
* `export namespace` 级联到命名空间内的每个定义。
* 定义的 `export` 标记**只在构建库时**被消费：export 标记的定义（加上其签名引用的类型、每个全局变量、每个顶层 `impl X for Y` 边，以及含模板/元构造文件的逐字源码）进入库的元数据；其余一切对编译出的 `.so` 私有。普通（非库）编译忽略 `export`。

完整保留集规则见[模块化编程](./10_ModularProgramming.md)的库一节。

## 源文件
PenguinLang 以 `.penguin` 为源文件扩展名，对文件与目录没有任何限制。

## 工程
PenguinLang 支持单文件编译；更大的软件需要工程文件。工程文件用 `.penguins` 扩展名、INI 风格格式：一个 `[Project]` 节、`key="value"` 对；空行与 `#` 注释被忽略。
```
[Project]
# 单引号数组风格：sources、libs 与 flags 都是字符串数组
name="MyPenguin"
sources=[
	"a.penguin",
	"b.penguin",
	"src/**/*.penguin"
]
libs=["../shared/libfoo.penguin-lib"]
flags=["-enable-coroutine"]
```
键：
| 键 | 值 | 含义 |
| --- | --- | --- |
| `name` | 带引号字符串 | 工程名 |
| `sources` | 字符串数组 | 源文件/glob 模式（`*`、`**`、`?`），相对工程文件解析 |
| `lib` / `libs` | 字符串数组 | 要链接的共享库（`.penguin-lib`；见[模块化编程](./10_ModularProgramming.md)） |
| `flags` | 字符串数组 | 编译器选项 token（不以 `-` 开头的条目被忽略）；工程 flag 等效于命令行给定 |
| `meta-sources` | 字符串数组 | 对编译期 `#fun` 代码可见的额外源文件（见[元编程](./11_MetaProgramming.md)） |

工程文件绝不能注入 `sources` 之外的源文件；相对 `libs` 路径相对工程目录解析。把 `.penguins` 文件代替 `.penguin` 文件传给编译器即编译整个工程。
