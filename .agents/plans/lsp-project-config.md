# 新 LSP（MagellanicPenguin/LspServer）修复计划

日期：2026-08-30。分支：从 `feature/penguin-scheduler-migration` 切出 `feature/lsp-project-config`（LSP 提交 6572fcf 在此分支上）。

三个问题的修复计划（用户已确认的设计决策见文末）：

1. `_utils` 仅作 EmperorPenguin 自举工具，LSP 作为用户程序应使用 std 库
2. 新 LSP 无 project 处理导致实际无法使用：与老 LSP 相同向上找 `.penguins`；优先级最高的是 vscode 根目录的 `.magellanic.config`（JSON，按目录配置编译参数）
3. 重构不优雅的代码（如 LspQuery 的大量手写 JSON 工作），合理利用 meta 功能，分离组件减少耦合

分四个里程碑（各自独立提交、保持绿色），外加测试基建先行。

## 里程碑 0：测试基建（runner 小改 + fixtures）

LSP 会话测试需要磁盘上的多文件项目和已知路径，当前 `Tests/Program.cs:1864` 直接把 `Stdin` 喂给进程、不经过 `EnvHelper.Expand`：

- `RunStageAsync` 中对 `test.Run.Stdin` 先做 `EnvHelper.Expand(stdin, workDir)`，使 `${PENGUIN_ROOT}`/`${WORKDIR}` 可用于 LSP 会话的 uri；在 `Tests/Readme.md` 的 Run LSP 节注明
- 新增 fixtures：
  - `Tests/fixtures/lspproj/`：`proj.penguins`（sources 引用两个文件）+ `main.penguin` + `libx.penguin`（跨文件符号：main 调用 libx 的函数）
  - `Tests/fixtures/lspcfg/`：根放 `.magellanic.config`（数组形式）+ 子目录项目 + 一个"诱饵" `.penguins`（用于验证 config 优先级）

## 里程碑 1：_utils → std 迁移 + didClose 修复

现状：LSP 自有代码 ~70 处 `_utils.List`（SymEntry.children、候选路径、token 列表等）。原则：**LSP 作为用户程序，自有数据结构一律 std；`_utils` 类型仅允许出现在编译器 API 边界**（`compile_sources` 的入参 `_utils.List<SourceInput>`、`result.errors`、`BoundDefinition` 列表、Lexer tokens——这些是 lib 内嵌源码的固定签名，改编译器不在本次范围）。

- `LspQuery.penguin`（SymEntry、各查询内部列表）、`LspCompilationUnit.load_stdlib_text` 的候选列表、其余模块中 LSP 自有的 `List` → `std.Vector`（API 兼容：push/at/set/size/for-in 均在；LSP 未用 pop/remove，无缺口）
- `LspMain` 修 bug：didClose 时 `units.remove(uri)`（`std.HashMap` 有 remove；当前重开同一 uri 会写入已退役 unit 的死 Fifo）
- 验证：`make lsp` 后 16 个现有 `Tests/LspTest/*` golden 字节级不变

## 里程碑 2：类型化协议 + 模块拆分（meta 重构）

- 新增 `LspProtocol.penguin`：LSP 输出结构体 `LspPosition/LspRange/LspDiagnostic/LspLocation/LspTextEdit/LspCompletionItem/LspInlayHint/LspDocumentSymbol`（children 递归 `std.Vector`），类体内用 json.penguin 的 `#impl_json_serializable();`（meta 反射 `t.fields()` 自动生成序列化；pass4 是 JIT-capable，LSP 构建已用它）。字段声明为 `mut`（生成反序列化需要写入），字段全为 i64/string/Vector——避开已知限制（f64 精度、Option 运行时分发不可达）
- 新增 `LspDiagnostics.penguin`：SemanticError 列表 / 编译器 panic → `Vector<LspDiagnostic>` → 单一序列化路径，消灭 `LspCompilationUnit` 里两段重复的手写 JSON（publish_error_diagnostics / publish_crash_diagnostic）
- 拆分 `LspQuery.penguin`（988 行）：
  - `LspSymIndex.penguin`：SymEntry 树构建（index_defs/flat_collect/function_detail）——纯索引层
  - `LspQuery.penguin`：纯查询逻辑，返回类型化结构体（不再内嵌 JsonWriter），入参改为文档上下文结构体（path/text/last_ok/符号索引缓存）而非整个 actor，解耦查询与通道机制
- `LspJson.penguin` 瘦身为：帧级消息 parse/serialize、uri↔path 转换、少量参数提取（稳定的形状可用类型化反序列化替代手写 `.get().some.get()` 链）
- 硬约束：对外 JSON 字节级不变（所有现有 golden 不动），逐测试验证

## 里程碑 3：project 发现（.penguins 向上查找）

- 新增 `LspProject.penguin`（唯一触碰 `_utils.List<SourceInput>` 的模块，编译器边界适配层）：
  - `find_project_file(dir)`：从文档目录向上 ≤10 层找 `*.penguins`（`std.io.dir_entries`），与 C# LSP `FindProjectFile` 一致；找不到 → 单文件回退（现状行为）
  - 项目编译：`emperor.PenguinProject.load/resolve_sources`（lib 内嵌、运行时可用）解析 glob → SourceInput 列表 = stdlib（core_builtin + io + scheduler）+ 项目源文件；**同项目所有已打开文档用内存文本**（用户已确认），未打开的 `std.io.read_text` 读盘——LspMain 在转发 didChange/didOpen 前把 units 的 uri→text 快照刷给对应 unit（避免 LspCompilationUnit↔LspMain 循环引用）
  - project flags 应用（镜像 main.penguin:31-55）：`--enable-coroutine`、`-D` defines、`-v`；coroutine 默认保持开启（分析超集，无害）
  - project libs：`emperor.load_lib_chain` + `compiler.lib_instances` + lib SourceInputs 合并（镜像 main.penguin:138-147/193-197），`LibLoadState` 按 lib 路径列表缓存（每次重编译不重复解析 .so 元数据）
- 诊断过滤到当前文档（按 `error.location.filename == doc path`）——项目编译后其他文件的错误不再错位挂到当前 uri
- 测试：`LspTest/ProjectDiscovery.md`（跨文件 goto-def 生效）、`DidCloseReopen.md`（didClose 后重开正常）

## 里程碑 4：.magellanic.config（优先级最高）

- 新增 `LspConfig.penguin`：数组形式（用户已确认）：

  ```json
  { "projects": [
      { "dir": "EmperorPenguin", "project": "EmperorPenguin/EmperorPenguinPass1.penguins" },
      { "dir": "MagellanicPenguin/LspServer", "project": ".../LspServer.penguins",
        "args": ["--enable-coroutine"], "libs": ["build/linux/libemperorpenguin.penguin-lib"] }
  ] }
  ```

  `std.parse_json` 解析；`initialize` 时从 params 取 `rootUri` 存入 LspMain，仅从 `<rootUri>/.magellanic.config` 读取（用户已确认：仅工作区根，不向上回退），会话内加载一次
- 路由：文档路径对 `dir` 键最长前缀匹配（相对 root 归一化）→ 命中条目的 `project`（相对工作区根）+ `args`（覆盖/追加于项目自带 flags，config 优先）+ `libs`（相对工作区根解析）
- 优先级：config 命中 > .penguins 向上查找 > 单文件回退
- 仓库根新增 `.magellanic.config`（dogfooding：EmperorPenguin 各项目 + LspServer 带 lib，让本仓库自身开发可用）
- 测试：`MagellanicConfig.md`（config 路由生效）、`MagellanicConfigPriority.md`（config 压过更近的诱饵 .penguins）

## 收尾

- `LspServer.penguins` 与 `LspServerWin.penguins` 的 sources 列表加入全部新模块（win 侧编译器源码仍列 utils.penguin——那是编译器自身的依赖，不属于 LSP）
- 更新 `MagellanicPenguin/LspServer/README.md`、`.agents/memory/lsp-next-session.md`
- 全量验证（tee 到 /tmp/test.log）：`make lsp`（需 `make bootstrap` 的 pass4）→ `--filter LspTest/*` → `make test` 全矩阵 → `dotnet test`（runner 改动）→ `make lsp_win` 交叉构建

## 风险与对策

- `#impl_json_serializable` 对递归 children / 生成 impl 的边界情况若有坑：该结构体退回手写 writer（仍走统一类型化出口），不阻塞其余重构
- initialize 响应字节必须不变（SessionLifecycle golden 锁死）——rootUri 解析只存不答
- 每键全项目重编译 = 旧 LSP 同款同步模型（无防抖）；性能对齐旧 LSP 即为达标，防抖/按项目共享编译结果列为后续
- 若旧 LSP 有而新 LSP 需对齐的行为差异在实现中再现，按 AGENTS.md 以最小 repro 落 `Tests/<Category>/*.md`

## 用户已确认的设计决策（2026-08-30）

1. `.magellanic.config` 用数组形式：`{"projects": [{"dir": "...", "project": "...", "args": [...], "libs": [...]}]}`，目录键相对工作区根，最长前缀匹配
2. `.magellanic.config` 仅从工作区根（initialize 的 rootUri）读取，不向上回退
3. 项目编译时同项目所有已打开文档用内存文本（超出旧 LSP：旧版仅当前文档），未打开的读磁盘
