# EmperorPenguin io 标准库设计

Date: 2026-08-21
Status: implemented (see git history on feature/io-stdlib)

## 目标

为 EmperorPenguin 提供一套 `io` 标准库（console/stdin/stdout、文件句柄、整文件读写、
文件系统查询），随 stdlib 自动加载，由 pass3（及 pass2/pass4，同 native runtime）编译运行。

## 架构事实（探索结论）

1. **stdlib 加载点**：`EmperorPenguin/main.penguin` 用 `_utils.file_read_text` 读
   `EmperorPenguin/std/penguin/core_builtin.penguin` 并 push 进 `inputs`。io 库照搬此路径
   （`io.penguin`），因此修改 main.penguin 后必须重新 `./penguin -b` 自举。
2. **extern 符号路由**：`LLVMEmitter.lower()` 只对 `__builtin.*` / `_utils.*` 命名空间的
   extern 加 `_emperor_` 前缀（`compute_extern_tail` + `sanitize_name`）。所以 io 的 extern
   必须声明在 `namespace __builtin` 里（io.penguin 内追加 `namespace __builtin {}` 块，
   命名空间可跨文件合并），名字尾即 C 符号尾：`io_file_open` → `@_emperor_io_file_open`。
3. **未引用的 extern 免费**：不使用的 extern 不会进入 IR，不产生链接依赖
   （`_malloc` 注释先例）。io.penguin 随每次编译注入也不会污染用户程序。
4. **用户程序编译时没有 `_utils`**：用户程序只加载 core_builtin(+io)。所以 io.penguin 只能
   依赖 `__builtin`（Option/Result/StringBuilder/IIterator/cast 等）与自身定义。
5. **BabyPenguin 兼容性（pass1 生命线）**：`penguin -1` / test-runner 的 Pass1 是
   EmperorPenguin 源码跑在 BabyPenguin VM 上，其 main.penguin 会加载 io.penguin 并由
   BabyPenguin SemanticCompiler 编译。io.penguin 只能使用 core_builtin.penguin 同级的语法
   （namespace、extern、class + mut 字段、enum、`impl IReferenceType`、`impl IIterator`、
   `#template`），保证 pass1 全测试套件不回归。
6. **字符串转义**：`"\n"` 字面量在 LLVMEmitter.unescape_string 与 BabyPenguin
   UnescapeString 双侧支持，可安全用于行分隔。
7. **自举联动**：bootstrap pass2→pass3、pass3→pass4 的编译输入也含 stdlib（main.penguin
   push），即 io.penguin 会被 pass2/pass3 编译进自举流程；收敛校验要求 pass3/pass4 一致。

## API 设计（`namespace io`）

### Console
- `io.print(s)` / `io.println(s)` / `io.eprint(s)` / `io.eprintln(s)`：薄封装 `__builtin`
- `io.read_line() -> Option<string>`：stdin 一行（无换行符），EOF → none
- `io.read_all() -> string`：整个 stdin
- `io.stdin_lines() -> StdinLineIterator`：惰性逐行迭代（for-in）

### 文件句柄 class io.File（IReferenceType + IMemoryDispose）
- `io.open(path, mode) -> Option<File>`：mode 为 C fopen 语义（"r"/"w"/"a"/...），失败 → none
- `File.write(s) -> bool` / `File.write_line(s) -> bool`（追加 "\n"）
- `File.read_line() -> Option<string>` / `File.read_all() -> string`
- `File.flush()` / `File.close()` / `File.is_open() -> bool`
- `File.seek(pos) -> bool` / `File.tell() -> i64`
- `File.dispose_mem()`：GC finalizer 自动 close（IMemoryDispose 挂 class metadata 析构槽）

EOF 协议（避免 feof 假阳性）：先查 eof（已耗尽 → none），再读行；若结果为空串且读后 eof
→ none；否则 some(line)。正确处理无尾换行文件/空行/空文件。C 侧用 fgetc 循环，
'\r' 一律丢弃（CRLF 容忍）。

### 整文件与文件系统（namespace 函数）
- `io.read_text(path) -> Option<string>`（不存在 → none；存在 → some(内容)）
- `io.write_text(path, text) -> bool` / `io.append_text(path, text) -> bool`
- `io.exists/is_file/is_dir(path) -> bool`
- `io.mkdir(path) -> bool`、`io.remove(path) -> bool`、`io.rename(from, to) -> bool`
- `io.size(path) -> i64`（-1 = 不存在）
- `io.dir_entries(path) -> string`（"\n" 连接；配合 io.split_lines 迭代）

### 迭代器（充分利用 for-in/IIterator 语法设施）
- `io.lines(path) -> StringLineIterator`：整文件读入后逐行（CRLF 容忍，丢弃尾空行）
- `io.split_lines(s) -> StringLineIterator`：任意字符串按行迭代
- `io.stdin_lines() -> StdinLineIterator`：stdin 流式逐行
- 形制照抄 core_builtin.RangeIterator：裸 `next(mut this)` + `iter(this)` +
  `impl IIterator<string>`（双 next），值类型分类即可（无 IReferenceType）

## C runtime（core_builtin.c 新增，全部 `_emperor_io_*`）

console: `io_stdin_read_line` / `io_stdin_eof` / `io_stdin_read_all`
handle: `io_file_open`(FILE*→i64, 0=失败) / `io_file_close` / `io_file_write` /
  `io_file_read_line` / `io_file_eof` / `io_file_flush` / `io_file_read_all` /
  `io_file_seek`(绝对偏移) / `io_file_tell`
fs: `io_write_text` / `io_append_text`（返回成功 bool）/ `io_remove` / `io_rename`

复用已有 C 符号（在 `__builtin` 里声明同名 extern 即可路由）：`file_read_text` /
`file_exists` / `dir_exists` / `file_size` / `dir_get_entries` / `mkdir`。

GC 约定：所有返回 string 的 C 函数用 `_emperor_gc_alloc` 分配；FILE* 句柄以 u64 存于
File 对象，GC 不扫描（非 GC 堆指针），泄漏由 IMemoryDispose finalizer 兜底。

## 验证

- `./penguin -b` 自举（pass2/3/4 编译 io.penguin，收敛）
- `penguin -3` 编 Examples smoke（含 pass1 `-1` hello world 回归 —— BabyPenguin 兼容）
- `Tests/StdlibTest/Io*.md`：Apply To = EmperorPenguin Pass3，覆盖 console/stdin/
  File 读写/EOF/整文件/append/fs/目录/迭代器/seek-tell
- `dotnet run --project Tests/PenguinTestRunner -- --filter StdlibTest/Io* --compilers pass3`

## 更新（2026-08-21 第二轮）：std 命名空间 + 通用 mangle 路由

1. io 库移入 `namespace std { namespace io {...} }`，API 全部变为 `std.io.*`。
2. 前置修复：`bind_member_access` 新增 member_access 基的链式命名空间查找
   （此前深度 ≥2 的限定调用全部 E_INTERNAL no callee symbol；BabyPenguin 自身仍不支持，
   回归测试 NestedNamespaceAccess 仅 Pass2/Pass3）。
3. 通用 extern→C 路由（mangle 方案）：`std.` 前缀 extern 映射
   `@_emperor_<点号→下划线>`，全限定名键 + llvm_func_name 两遍探测（精确优先/tail 兜底）。
   `__builtin`/`_utils` 旧行为字节级不变；std 外用户 extern 保持字面符号。
   C 侧 16 个函数重命名 `_emperor_std_io_*` + 6 个旧实现薄别名。
4. extern 名与 API 名解冲突（write_text→file_write_text、mkdir→file_mkdir 等）。
   std.* extern 必须限定调用（io.penguin 内全部 `std.io.X()`）。

## 更新（2026-08-21 第三轮）：通用规则去特例化（用户要求）

1. 移除 `std.` 前缀特例。**通用 extern→C 规则**（无任何库特例）：
   - 命名空间 extern（std/emperor/用户任意 ns）→ `@<全限定点号名 sanitize（. → _）>`
     （`std.io.file_open` → `std_io_file_open`；`mylib.foo` → `mylib_foo`；
     `emperor.penguin_meta_*` → 保持原路径不变）
   - 顶层裸 extern → `@<裸名>`（字面 libc 符号；`extern fun abs` → `@abs`）
   - `__builtin`/`_utils` 运行时命名空间 → 历史 `_emperor_<tail>`（唯一保留的特例）
2. 顶层 extern 声明豁免文件命名空间（`top_level_def_scope`）：C 符号天然全局，
   `_ns_<file>_<hash>_<name>` 不稳定不可实现。
3. C 符号二次重命名 `_emperor_std_io_*` → `std_io_*`（16 函数 + 6 别名）。
4. llvm_func_name 恢复单遍 tail 探测（extern_map 仅剩 builtin tail 索引，
   其余走 fallthrough 即通用规则）。
5. 新测试：GlobalExternLibcTest（libc abs）、NamespacedExternMapping
   （用户 ns `_emperor.gc_info` 纯名字算术命中 runtime 符号）。
