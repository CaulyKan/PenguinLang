# EmperorPenguin Port 语法 LSP 服务器（重构 MagellanicPenguin LSP）— 执行计划

> 2026-08-26 启动。前置：feature/ep-rtl-ports E0–E6 已完成（port/channel/coroutine 全绿，Apply To BabyPenguin+Pass3）。
> 本计划 = rtl-ports-design.md §实战检验 LSP 六模块推演的真正落地：用 port 语法重写 MagellanicPenguin LSP（现 C# LSP.cs 718 行），pass3 编译原生二进制。
> 架构（用户指定）：StdioStream（stdin 读=output 端口，stdout 写=input 端口，底层 Linux fd 异步）→ JsonInputParser（解析 JSON-RPC，demux 出多个 lsp command 输出端口）→ LspMain（会话级命令：initialize/shutdown/exit + didOpen 动态创建 LspCompilationUnit）→ LspCompilationUnit（每文档：编译、诊断、go_to_definition 等查询）→ JsonOutputParser（汇聚 Result/event 流，序列化帧）→ stdio。模块间 FIFO/wire 连接。

## 关键侦察结论（2026-08-26）

1. **调度器静止即退出**（scheduler.c 无外部事件源，v1 文档化偏差）——LSP 必须扩展：fd waiter 集成（poll 于静止时阻塞，事件作为外部 delta 注入）。这是 L1 的核心运行时工作。
2. **connect 汇端只支持 input 端口 / MultiInput**（偏差 F，`connect(port, fifo)` 不支持）。应对：
   - 静态会话骨架用端口直连（wire）——生产者循环「wait 输入→处理→写一次」每轮每输出至多一写，wire 跨轮事务保序不丢（FanoutIndependentCursors 语义），安全；
   - 动态 per-document unit 用 `fun new` 构造器传 `mut ISource<LspMessage>` / `mut ISink<LspOutMsg>` 通道视图（设计追问四轮-1 正交原则：fun new=数据参数含通道视图，construct=静态接线糖）——每 unit 一条专属 Fifo，汇入 JsonOutputParser 的 `MultiInput<LspOutMsg>` 字段（N:1 扇入，运行期 add() 合法）。
3. **测试 runner 已支持 `Stdin:`**（Run 阶段，C 转义可写多行）——LSP e2e 可直接喂 JSON-RPC 帧断言 byte-exact stdout。缺「预编译 exe」backend（L3 给 runner 加 exe backend，server e2e 测试不用每次重编 16k 行）。
4. **json.penguin**（std，pass3-only，随 hashmap/vector 编入）提供 JsonValue 树 + JsonReader/JsonWriter + parse_json——解析/序列化直接用。注意：编译 LSP 时不列 core_builtin/io（编译器自动注入），但 json/hashmap/vector 要显式列（同 EmperorPenguinFull.penguins）。
5. **嵌入式编译器**：LSP 程序内直接 `new EmperorPenguinCompiler()` + `compile_sources(inputs)`（SourceInput 列表，core_builtin+io 源文本 + 文档文本），enable_coroutine=true（文档可能用 port）。stdlib 定位：先 exe-dir 相对，后 cwd 相对（main.penguin 是 cwd 相对）。项目发现：从文档目录向上找 *.penguins（≤10 层，同 C# LSP），PenguinProject.load + resolve_sources，非活动文档用磁盘内容。
6. **definition 查询**：BoundIdentifierExpression 无 location，但所有 BoundSymbol 均带 location。方案：Lexer 重扫文档取光标处 token → 名称 → 符号索引（BoundCompilationUnit.definitions 递归 walk，name→location/kind）优先同文件匹配。v1 非作用域精确，可接受（C# 版同样朴素）。documentSymbol / completion 复用同一索引。
7. **C# LSP 功能面**（对齐目标）：initialize（capabilities: full-sync + completion/documentSymbol/definition）、didOpen/didChange/didClose→publishDiagnostics（每次全量重编，保留 last-success 供查询）、completion（关键字+符号+类型）、documentSymbol、definition、shutdown/exit。已知怪癖不搬：references 假广告、shutdown 后 1s 强杀、logMessage 刷屏。
8. **exit 时序**：exit 通知 → LspMain 向结果流写 `eof_exit(code)` 控制事务 → JsonOutputParser 顺序转发到帧流 → StdioStream 写完之前的所有帧后写控制标记 → `exit(code)`（帧 FIFO 序保证响应先落 fd）。stdin EOF（无 exit）→ 各模块 park 于死源 → 静止退出 0（已排队事务先被消费干净）。

## 分阶段（每阶段独立验证 + 提交）

### L1 — C 运行时 fd 外部事件（scheduler.c + core_builtin.c + __builtin externs）
- scheduler.c：fd waiter 链表（fd + 读/写兴趣）；`_emperor_fd_wait_read/write(fd)` park 当前协程进 fd 表（不进轮队列）；静止分支先查 fd waiter：有 → poll(所有 fd, timeout=-1 或有 timer 时 0) → 就绪者出队重入 ready → continue；fd waiter 存在时永不 fingerprint 退出。EINTR 重试。
- core_builtin.c：`_emperor_read_fd(fd) -> string`（单次 read ≤32KB → GC 串，EOF/err→""）；`_emperor_write_fd(fd, s) -> i64`（单次 write，EAGAIN→0，err→-1）；fd 0/1 首用置 O_NONBLOCK。Windows #ifdef 桩。
- core_builtin.penguin：`__builtin` externs `_fd_wait_read/_fd_wait_write/_read_fd/_write_fd`（universal 命名 → `_emperor_*`）。
- 测试：`Tests/LspTest/FdEchoChunk.md`（Pass3 + Stdin，分块读回显直到 EOF）、`FdTimerWithFd.md`（timer+fd 共存）。
- 验证：全 coroutine 套件（PortTest/ChannelTest/…）无回归（无 fd waiter 路径零改动）。

### L2 — LSP 源骨架：LspTypes + 帧 + StdioStream 模块
- `MagellanicPenguin/LspServer/`：`LspTypes.penguin`（LspMessage/LspOutMsg/LspResponse/LspNotification/LspFrameAction）、`LspFraming.penguin`（纯函数：缓冲积累 + Content-Length 提取，可单测）、`StdioStream.penguin`（rx:string 输出端口 / tx:string 输入端口，双 initial：读循环 fd_wait_read+read_fd、写循环 POLLOUT 反压 + eof_exit→exit(code)）。
- `LspServer.penguins`：Full 同款源列表（相对路径 `../../EmperorPenguin/...`）+ json/hashmap/vector + LSP 源。
- 测试：帧解析单测 md（Pass3，喂切块/半头帧）；StdioStream 回环 md。
- 构建命令：`tmp/pass3 --enable-coroutine MagellanicPenguin/LspServer/LspServer.penguins -o tmp/lsp`（penguin 脚本加 `-lsp` 目标）。

### L3 — 六模块接线：lifecycle + diagnostics 全链路
- `JsonInputParser.penguin`（input chunks:string → parse_json → LspMessage → 输出端口 session + docs demux）。
- `LspMain.penguin`（input session:LspMessage + input docs:LspMessage；initialize→capabilities 应答；shutdown→null 应答+标记；exit→eof_exit(0/1)；docs 路由：uri→unit 表，didOpen 动态 new LspCompilationUnit（Fifo 双向）+ MultiInput.add，didClose 转发+移除）。
- `LspCompilationUnit.penguin`（ctor(uri,text,cmds:ISink… actually cmds:mut Fifo 由 LspMain 持有，unit 拿 ISource 视图 + results:mut ISink<LspOutMsg>)；循环：didChange→重编→publishDiagnostics；didClose→break；查询请求→应答；嵌入式 compile（stdlib 发现+项目发现+enable_coroutine）。
- `JsonOutputParser.penguin`（inputs:MultiInput<LspOutMsg>；循环序列化 `Content-Length: N\r\n\r\n{...}` → tx）。
- 顶层 construct 接线（端口 wire + 结果 MultiInput）。
- runner 加 exe backend（`Apply To: Exe`，Compile.Args=exe 路径）→ `Tests/LspTest/SessionLifecycle.md`（initialize/initialized/didOpen(错文档→诊断)/shutdown/exit，byte-exact 帧）。
- e2e：诊断发布、shutdown/exit 时序。

### L4 — 查询功能：documentSymbol / definition / completion
- `LspQuery.penguin`：符号索引（definitions 递归：namespace/class/enum/interface/fun/field，name/kind/location/children）；documentSymbol 树；definition（Lexer 取 token→索引查同文件优先）；completion（关键字表+索引，kind 映射）。
- 测试：`Tests/LspTest/DocumentSymbol.md` / `GotoDefinition.md` / `Completion.md`（Prebuilt e2e）。

### L5 — 收尾：vscode 接线、文档、全矩阵
- vscode：extension.ts 已有 PENGUINLANG_LSPSERVER_PATH 覆盖点；加发布脚本把 tmp/lsp 复制进 server/linux（文档说明）。
- 文档：Documentation/11 更新（外部 fd 事件源落地→终止偏差 E 修订）；MagellanicPenguin/LspServer/README.md（架构图+接线说明）；.agents/memory 条目。
- 验证：`dotnet run --project Tests/PenguinTestRunner`（BP+Pass1 全量）+ 有 pass2/3 时全矩阵 + `dotnet test`。

## 风险与对策
- scheduler.c 改动影响全部协程程序 → 无 fd waiter 时代码路径不变，L1 全量回归把关。
- pass3 编 LSP（16k+ 行）慢（分钟级）→ 里程碑级构建，e2e 用 Prebuilt exe；L2/L3 单测只编小入口源。
- json/hashmap 的 # 构造 → 只能 pass3 编（不能 BP），单测 Apply To: Pass3。
- 嵌入式编译器全局静态跨编译残留（计数器/元数据）→ 测试观察，必要时每次编译前重置入口。
- ucontext 每协程 32MB 虚拟栈 ×每文档 unit → mmap 惰性提交，可接受。

## 完成记录 (2026-08-26 晚)
- **L1–L4 全部完成**，L5 完成（vscode `-p` 集成 + `-lsp` 目标 + 文档 + memory）。
- 额外修复（本任务暴露/踩到）：
  1. GC 扫描 live_lo 未对齐 → 跨 mmap 边界 SIGSEGV（gc.c set_live + collect 双点向下对齐；SessionLifecycle.md 是字节级红→绿锁，GcCollectWhileFdParked.md 是路径哨兵）。
  2. `_Fanout.subscribe` 把类型零值种子当可交付 → 订阅者 time-0 空事务假唤醒（StdioStreamEcho 捕获；EP/BP 双侧同修，`has_live_value` 判据）。
  3. runner Stdin 的 Replace 链破坏 JSON 内嵌 `\\n`（改单遍 C 解转义）+ 新增 `ESCAPE` 期望模式（stdout 可断言 CR 字节）。
  4. 测试侧：FramerSplitFrames 的 Apply To 曾含 BP（BP backend 忽略 Compile.Args，不可能绿）→ 改 Pass3-only。
- e2e: SessionLifecycle / DocumentSymbol / GotoDefinition / Completion 全绿（Prebuilt backend, `./penguin -lsp` 产物）。
- 遗留：EP 解析器对 `let x = ;` 静默恢复（诊断空）；enum member symbol 无 location（definition 落到 enum 处）；win32 原生 LSP（等 Windows fd 集成）。
