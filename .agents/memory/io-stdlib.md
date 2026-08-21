# io 标准库（2026-08-21，分支 feature/io-stdlib）

`EmperorPenguin/std/penguin/io.penguin`（`namespace std { namespace io {...} }`）+
`std/c/core_builtin.c` 的 `_emperor_std_io_*` 函数族 + `main.penguin` stdlib 自动加载。
pass2/3/4 原生可用；BabyPenguin VM 未实现（io 测试 Apply To 仅 Pass2/Pass3）。
设计全文见 `.agents/plans/io-stdlib.md`。

## 关键事实（复用时最容易踩的）
1. **通用 extern→C 规则**（第三轮去特例化：无库特例）：
   `LLVMEmitter.lower()` 对 `std.` 前缀的 extern 按 mangle 名映射
   `@_emperor_<点号→下划线>`（`std.io.file_open` → `@_emperor_std_io_file_open`），
   以**全限定名**为键；`llvm_func_name` 两遍探测（精确 key 优先、tail 兜底保持
   builtin 短/长调用形式）。新 stdlib 模块（std.net 等）在自己的 namespace 里声明
   extern 即自动获得 C runtime 路由。用户/库 extern（std 之外）保持字面 libc 符号
   （MetaEngine 的 `emperor_penguin_meta_*` 依赖此路径）。
   **std.* extern 必须限定调用**（`std.io.file_open`）——裸名走字面符号会链接失败。
2. **嵌套命名空间成员访问**（`std.io.x()` 的前置修复）：parser/scope 一直支持
   `namespace std { namespace io {} }`，但 `bind_member_access` 的命名空间分支只接受
   identifier 基表达式——内层 `std.io` 绑定为携带 namespace 符号的 member_access，
   深度 ≥2 的调用全部落入 void fallback（E_INTERNAL: no callee symbol）。修复：新增
   member_access 基的链式命名空间查找（SemanticBindExpressions，identifier 分支旁）。
   **BabyPenguin 自身编译器仍不支持**嵌套 ns 成员访问（Cant resolve symbol），
   回归测试 `Tests/NamespaceTest/NestedNamespaceAccess.md` 仅 Apply To Pass2/Pass3。
3. **stdlib 加载点**：`main.penguin` 读 `EmperorPenguin/std/penguin/{core_builtin,io}.penguin`
   push 进 inputs —— 改 main.penguin / 编译器源码必须 `./penguin -b` 重建；io.penguin
   本身改动不需要（每次用户编译时从磁盘读，相对 repo root cwd）。
4. **EOF 协议**（feof 假阳性坑）：read = fgetc 循环（最多多读一字节）；PenguinLang 层
   `eof()` 先查（流已耗尽 → none），读回空串再查 eof（真耗尽 → none）。无尾换行最后一行
   恰好交付一次。C 侧 '\r' 一律丢弃（CRLF 容忍）。
5. **std.io.File 引用语义**：`impl __builtin.IReferenceType`（防值拷贝）+
   `impl IMemoryDispose`（GC finalizer 自动 close）。FILE* 以 u64 存放，GC 不扫描。
6. **命名冲突规避**：extern 名不能与同 namespace 的 API 函数同名（write_text 等
   → extern 用 `file_write_text`）；`mkdir` API vs `file_mkdir` extern 同理。
7. **C 别名**：查询类 extern（file_read_text/exists/size/dir_get_entries/mkdir）复用
   旧 `_emperor_*` 实现，C 侧 `_emperor_std_io_*` 薄别名（_utils 仍绑旧符号，两名并存）。
8. **自举联动**：bootstrap pass2→pass3 编译输入注入 stdlib（io.penguin 被每个原生
   pass 编译），收敛校验天然覆盖。
9. **test-runner Stdin 转义**：`Stdin: `a\nb\n`` 支持 `\n`/`\t`/`\r`/`\\`。

## 验证命令
```
./penguin -b                                   # 收敛（pass3==pass4 md5）
dotnet run --project Tests/PenguinTestRunner -- --filter StdlibTest/Io* --compilers pass3
dotnet run --project Tests/PenguinTestRunner -- --filter NestedNamespaceAccess --compilers pass2,pass3
dotnet run --project Tests/PenguinTestRunner --    # 全量回归
```
