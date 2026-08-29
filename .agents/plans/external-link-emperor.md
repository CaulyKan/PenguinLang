# 构建系统重构：外置链接 + 分平台 release + 文件级目标

日期：2026-08-29　分支：`feature/external-link-emperor`（自 `feature/ep-lsp-ports` 切出）

## 目标（用户需求）

1. `make release` 产出 `build/linux(win)/emperor_penguin_llvmir_emitter(.exe)`。
2. 各 pass 输出为文件目标（如 `make build/bootstrap/pass2`）；`bootstrap` 依赖 `build/bootstrap/pass4` 并额外编 pass5 检查收敛；`release` 依赖 pass4；`lsp` 依赖 release 并复用 release dynlib。
3. 为编译器定义文件级依赖，文件未变不重编。
4. 从 EmperorPenguin 源码删除 LLVM 调用；emitter 只产 `.ll`；`EmperorPenguin/emperor(.bat)` 负责检查 LLVM 环境、链接产物、交叉编译，release 时拷贝到 `build/linux(win)/`。
5. 分平台目标 `release_linux` / `lsp_win` 等；`make release` 按宿主自动选择；`make publish` 依赖全部四个。`.ll` 平台无关（只统一 `_setjmp` 形状即可），两平台复用同一 `.ll` 只差链接与 C 运行时。
6. `make bootstrap` / `make test` 只做宿主平台，永不交叉。

**编号约定（与现状一致）**：pass1 = dotnet cs 阶段（无独立目标），文件目标从 `build/bootstrap/pass2` 起；pass2 ≡ 旧 `build/pass2`，pass4 = 交付（lib+exe 对），pass5 = 收敛。总代数与今天相同。

## 关键事实（核实于源码）

- 外部调用全部在 `EmperorPenguin/src/llvm/LLVMCompiler.penguin`：build_runtime(:36)、link_exe(:99-235, llvm-config :114, clang :228)、link_lib(:241-301, clang :282 + footer 追加)、write_export_def(:58)。
- `-enable-meta` 只影响链接（从不传入 EmperorPenguinCompiler）；`--enable-coroutine` 是语义 flag。
- `.ll` 平台差异仅 `_setjmp` 形状（LLVMEmitter.penguin:835-838 / :3494-3500，`windows_target` 仅此两处）。恒 2 参形式在 glibc 下安全（多余寄存器参数被忽略）。
- `main.penguin:76-81` 按 CWD 相对路径读 stdlib → 脚本 cd 到 root 解决；发布布局需捆绑 `std/c`（脚本要 `make -C std/c`）。

## 实施内容

1. **源码 emitter 化**：LLVMEmitter 删 `windows_target`、`_setjmp` 恒 2 参；LLVMCompiler 只留 build_ll（路径参数化）+ write_export_def（发射期总产出 `<out>.def`）；main.penguin：exe 模式 `-o X` → `X.ll`+`X.def`，lib 模式 `-o X.penguin-lib` → `X.ll`+`X.libmeta`；`-target/-enable-meta/-llvm-win` 继续解析但 no-op。
2. **脚本**：`EmperorPenguin/emperor`（bash）+ `emperor.bat`。子命令：全流程 / `link <ll> -o out [-enable-meta] [-target=win64] [-llvm-win D] [--consumer-lib so]` / `link-lib <ll> <libmeta> -o out.penguin-lib`（soname + PENGUINLIB footer 追加）。root/emitter 定位、环境检查、交叉 env（MINGW_PREFIX 等内部默认）、逐分支复刻链接 flag、OPT 透传。
3. **Makefile**：sed 解析 `.penguins` 源集为依赖；bootstrap 文件目标链（pass2.ll→pass2→pass3.ll→pass3→pass4.d lib/ll/libmeta/exe.ll/exe+symlink）；bootstrap=pass4+pass5.d 收敛（保留作证据）；release 共享 `build/release/*.ll` 双平台链接；lsp_linux 复用 release lib（cp 到 build/ 旁 build/lsp）；lsp_win 单体；publish 部署 emitter+脚本+std/{penguin,c}+DLL，保留 LSP 冒烟并新增 linux emperor 冒烟；删 TARGET/XENV/lsp-cache。
4. **测试运行器**：二进制路径 `build/bootstrap/pass2|3`；每个 Emperor 编译后串接 `emperor link out.ll -o out.exe`；工件 out.ll。
5. **扩展**：emperorPenguinPath → `server/linux/emperor` / `server\windows\emperor.bat`。
6. **文档**：AGENTS.md 构建章节、README、Tests/Readme.md。

## 验证

`make clean && make bootstrap` 收敛且二次近零编译；`make test` 全绿（setjmp 统一的风险把关）；`make release` / `make release_win` 复用同一 .ll；`make lsp` / `make lsp_win`；`make publish` 冒烟。bug 一律固化为 `Tests/*.md`。

## 风险

glibc 2 参 `_setjmp`（测试把关，回退=保留 -target 差异）；emperor.bat 无法本机运行验证（对齐 bash 分支 + 产物存在性检查）。
