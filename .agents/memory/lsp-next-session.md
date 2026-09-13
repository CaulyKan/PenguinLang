# LSP next-session work log (2026-08-27, session 3 — win32 native LSP)

Branch feature/ep-lsp-ports. The last open item from /tmp/state.txt — the
win32 fd/coroutine branch — is DONE and runtime-validated under wine. Also
fixed a Windows-target sjlj ABI bug in the emitter found by that validation.

## Windows native runtime (scheduler.c / core_builtin.c)

- Coroutines are Win32 FIBERS now (`EMPEROR_WIN_FIBERS`): `CreateFiberEx`
  (256 KB commit / 32 MB reserve, FIBER_FLAG_FLOAT_SWITCH) + `SwitchToFiber`;
  the scheduler home is the main thread converted via `ConvertThreadToFiber`
  (ERROR_ALREADY_FIBER → GetCurrentFiber reuse). The old `_WIN32` sequential
  no-op fallback is gone (kept only for POSIX-without-ucontext).
- GC interop carries over unchanged (single-threaded discipline: user code
  runs on exactly one fiber at a time):
  - fiber stack registered as a scan region by the TRAMPOLINE at first switch
    (one `VirtualQuery` on a local gives reservation base = mbi.AllocationBase,
    committed base = mbi.BaseAddress, top = +RegionSize; initial live range =
    [committed_base, top) so reserved-uncommitted pages are never scanned;
    parks narrow to sp as usual, gc.c caps the running fiber at its sp).
  - the heap-allocated EmperorCoroutine struct is its own scan region (covers
    entry_arg before first run AND every park's register spill).
  - `setjmp(self->regs)` immediately before every `SwitchToFiber(sched_fiber)`
    — SwitchToFiber parks callee-saved registers in scheduler-private memory
    the conservative scan can't reach; the buffer is never longjmp'd.
  - fingerprint guards NULL block/stack_hi (fiber that never ran).
- fd integration: level-probed readiness — pipes via `PeekNamedPipe`
  (failure e.g. ERROR_BROKEN_PIPE ⇒ treat ready: the read delivers EOF =
  wake-on-HUP parity), console via `GetNumberOfConsoleInputEvents`, disk
  always ready; idle blocking is a 2 ms re-probe loop (no epoll-for-pipes on
  Windows). Write side always ready: anonymous pipes have no writable-space
  query — the BLOCKING `_write` IS the backpressure, equivalent for the LSP's
  strictly ordered single writer.
- `_emperor_read_fd`/`_emperor_write_fd` are real now (were stubs): wait-then-
  syscall model, `_setmode(fd, _O_BINARY)` (frame bytes must not be CRLF'd),
  chunked write (≤1 GB pieces for _write's unsigned count).

## Emitter fix: mingw x64 _setjmp is TWO-argument (LLVMEmitter.penguin)

Found via win64 CrashSurvival dying with c0000005 after longjmp: the emitter
emitted glibc-shaped `call i32 @_setjmp(ptr jb)`, but mingw-w64's x64
`_setjmp(jmp_buf, void* frame)` read garbage RDX as the SEH frame and the
paired `longjmp` unwound through it. Fix: `LLVMEmitter.windows_target` field
(set from `CompilerConfig.is_windows_target()` via `LLVMCompiler.windows_target`
in main.penguin) → declare/call `_setjmp(ptr, ptr)` with `ptr null` (the plain
non-SEH sjlj flavor clang itself uses for C setjmp). Validated by hand-patching
the win64 LSP's combined.ll + relinking BEFORE the source fix: CrashSurvival
became byte-exact with Linux. Linux codegen unchanged (flag defaults false).
The `make publish` smoke tests (linux + wine windows) now ALSO open a broken
document and require the 'internal compiler error' survival diagnostic — the
exact regression class this bug belonged to.

## Windows LSP delivery

- `MagellanicPenguin/LspServer/LspServerWin.penguins`: MONOLITH = LSP modules
  + the whole EmperorPenguinLib source set (dyn-lib pair is ELF-specific:
  SONAME/$ORIGIN/rpath/-rdynamic). Path-relative sources are fine —
  file_ns_name uses only the basename.
- `make lsp TARGET=win` builds it via build/pass4 + llvm-mingw env
  (WIN_CC/WIN_CXX/WIN_AR/WIN_CLANG overridable) →
  build/win64-lsp/MagellanicPenguinLSP.exe (~12 MB PE32+, imports only
  KERNEL32 + UCRT). `make publish TARGET=win` deploys it + stdlib bundle to
  server/windows/ and runs the wine smoke when `WINE=<path>` (or wine on
  PATH) is available.
- vscode client already pointed at server\windows\MagellanicPenguinLSP.exe;
  added a clear missing-binary error message. Version 0.0.7 + CHANGELOG.

## Validation methodology (IMPORTANT for future sessions)

- **Portable wine works**: Kron4ek Wine-Builds tar.xz (wine-11.16-amd64) →
  extract anywhere, `WINEPREFIX` MUST be under a dir you OWN (/tmp is
  root-owned sticky → wine refuses; ~/.winepenguin works),
  `WINEDLLOVERRIDES="mscoree,mshtml="`, `WINEDEBUG=-all`, `wineboot -i none`.
  Console PE exe runs fine headless.
- **cwd matters when testing the LSP**: stdlib discovery is cwd-first then
  exe-dir + upward walk. Run test exes FROM THE REPO ROOT or you get silently
  empty stdlib → clean diagnostics but missing cross-file symbols (println
  definition returns []). This wasted a long detour that LOOKED like an -O2
  miscompilation / monolith bug — it was neither. Byte-compare against the
  Linux golden from the same cwd before blaming the target.
- Full session replay: extract `Stdin:` from any Tests/LspTest/*.md with a
  single-pass unescape (`\\`→`\` FIRST-class citizen; naive chained replaces
  corrupt `\\n` inside JSON strings) and diff stdout.
- Results: fd echo (FdEchoChunks shape) byte-exact under wine; 13/14 sessions
  byte-identical linux-vs-win64 BEFORE the setjmp fix (only CrashSurvival
  crashed); with the fix (hand-patched IR) it is byte-exact too. After
  bootstrap the real rebuilt exe must be re-verified to 14/14.

## Sentinel bookkeeping

- PortPayloadEnumChannelCycle: GREEN on pass1/2/3 (rebuilt 8/27 toolchain),
  description rewritten from RED SENTINEL to regression lock, Apply To
  extended to Pass1/2/3. StdioStream.penguin's string-payload note updated:
  strings-on-wire is architecture now, not a compiler workaround.

## Build system migration (2026-08-27, session 4 — ./penguin → Makefile, tmp/ → build/)

The `./penguin` shell script is DELETED; the root `Makefile` is the single entry
point. ALL build artifacts moved `tmp/` → `build/` (gitignored; pass2/pass3/
pass4(+.d)/pass5.d, lsp, libemperorpenguin.penguin-lib, lsp-cache, win64,
win64-lsp, linux, testruns, *.log).

- Targets: `clean bootstrap lsp test baseline_test publish all` (all =
  bootstrap→lsp→test via recursive sub-makes, no false deps). `TEST_ARGS="..."`
  passes runner args to test/baseline_test. `LSP_NO_CACHE=1`, `WINE=<path>`,
  `WIN_CC/WIN_CXX/WIN_AR/WIN_CLANG`, `MINGW_PREFIX`, `LLVM_WIN_PREFIX` behave
  as before.
- `TARGET=win|linux` (default host; `publish` with no TARGET builds BOTH on
  linux, win-only on a win host). Cross is linux→win only. **bootstrap always
  targets the HOST** — the bootstrapped compiler is the build tool.
- **Windows native self-bootstrap is now structurally supported** (MSYS2
  make/clang + the vendored thirdparty/mingw-w64-x86_64-llvm-libs for
  -enable-meta; the Makefile errors early if the package is missing):
  - The win bootstrap chain stays Full-MONOLITH (pass2→pass3→pass4→pass5 exe
    md5 convergence) — the .penguin-lib pair is ELF-specific, so the linux
    lib+exe split does not apply.
  - Every win-native stage passes `-target=win64` (PE stack flag, TWO-arg
    _setjmp — one-arg crashes under mingw, see session 3) and the Makefile
    exports CC/CXX/AR/CLANG=clang* + CROSS=win64 (steers std/c's Makefile to
    the Windows-JIT path with thirdparty headers) + PATH+=<pkg>/bin (the
    libLLVM-22.dll must be loadable at process start).
- **Compiler fix that unblocked it**: `_utils.exec("${CLANG:-clang}", ...)` in
  LLVMCompiler.penguin resolved the link compiler via POSIX shell expansion —
  cmd.exe (system() on win) can't expand `${...}`. Now `_utils.getenv("CLANG")`
  (new extern; `_emperor_getenv` in core_builtin.c; C# twin in
  BabyPenguin/Utils.penguin **and** EmperorPenguin/src/utils.penguin — the
  BabyPenguin twin is the ONLY _utils source at pass1 level-1 because the
  bootstrap compiles just the Pass1 project pre-`--`; ExternLowerer +
  ExternFunctions register `_utils_getenv`/`_utils.getenv`). GOTCHA: adding an
  extern ONLY to EmperorPenguin/src/utils.penguin compiles level-1 with
  E_RESOLVE_SYMBOL on the first call site — the twin file is load-bearing.
- Project files renamed by the earlier WIP: `EmperorPenguin.penguins` →
  `EmperorPenguinPass1.penguins`, `EmperorPenguinFull.penguins` →
  `EmperorPenguinPass2.penguins` (byte-identical content). All references
  updated (Tests/Program.cs Pass1 backend, BatchCompiler, BoundTypeRegistryTest,
  comments). The runner's Pass2/Pass3 backends now expect build/pass2|pass3;
  LspTest md files use `Run Args: build/lsp`.

## Session 4 (2026-08-30, branch feature/lsp-project-config): _utils→std, typed protocol, project discovery, .magellanic.config

Plan: .agents/plans/lsp-project-config.md — all four milestones landed, each
an independent green commit (d15bade, e5c904a, 975510d, 0be6a58, b28671b).

- **_utils → std migration**: every LSP-own list is `std.Vector` now;
  `_utils.List` remains ONLY at the embedded-compiler API boundary
  (compile_sources inputs, result.errors, BoundDefinition/BoundStatement
  lists, lexer.tokenize returns). didClose bug fixed (LspMain removes the
  unit from the map after forwarding the close; reopening used to black-hole
  requests into the dead unit's Fifo) — locked by LspTest/DidCloseReopen.md.
- **Typed protocol layer**: LspProtocol.penguin structs serialize themselves
  (hand-written JsonWriter methods, byte-identical wire output); queries
  (LspQuery.penguin) take an LspDocContext snapshot (path/text/last_ok +
  prebuilt index — the unit rebuilds the index when last_ok is replaced) and
  return typed structs; LspDiagnostics is the single diagnostics pipeline;
  LspSymIndex holds the index build. The json.penguin `#impl_json_serializable()`
  meta auto-impl CANNOT be used here: containers of user classes
  (Vector<Kid>, HashMap<string,Vector<T>>, self-recursive children) fail with
  E_RESOLVE_TYPE on the spliced impls — red sentinel
  Tests/StdlibTest/MetaJsonVectorOfSerializable.md documents it (meta splice
  runs before the template interface instantiation with the user class is
  registered; plain nested-class fields DO work — MetaJsonContainers).
- **Project discovery**: LspProject.penguin — find_project_file walks up ≤10
  dirs (C# parity); plan_from_project mirrors main.penguin's project handling
  (flags re-enter CompilerConfig.parse, libs relative to project dir);
  LibLoadState cached per resolved lib-path list. Opened siblings contribute
  EDITOR text (LspMain flushes a path→text snapshot before each forwarded
  didChange — no back-reference from units to LspMain); unopened files read
  disk. Project-mode diagnostics filter to the requesting doc (no-location
  errors stay visible — cross-file resolve errors carry file=""/line=0).
- **.magellanic.config** (LspConfig.penguin): array form at the initialize
  rootUri ONLY, loaded once; longest-prefix dir routing; config args parse
  after project flags (config wins), config libs resolve against the root.
  Priority: config hit > .penguins search > single file. Repo root has a
  dogfooding config (EmperorPenguin→Pass1, LspServer→LspServer.penguins with
  --enable-coroutine + build/libemperorpenguin.penguin-lib).
- **Runner features**: Run Stdin expands ${VAR} (LSP sessions open real files
  via file://${PENGUIN_ROOT} uris); ExpectedStdout/ExpectedStderr operands
  expand env ${VAR} too — goldens carry ${PENGUIN_ROOT} inside echoed frames,
  and Content-Length headers must count the EXPANDED bytes (build frames from
  the expanded text, then string-replace the root when writing the md).
- **GOTCHAs found**: (1) cross-file TOP-LEVEL symbols do not resolve in
  EmperorPenguin (each file's top level is its _ns_ namespace) — multi-file
  fixtures/libs need an explicit `namespace`; (2) a .penguins sources entry
  must be project-dir-RELATIVE — absolute paths are silently dropped by glob
  resolution; (3) LspTest sessions need `--enable-coroutine`-style flags only
  at BUILD time; the dbg trick (compile a driver .penguins with the LSP
  sources + a Dbg.penguin printing via __builtin.eprintln) is the fastest
  way to ground-truth LspProject/compile behavior without instrumenting the
  server (copy sources into the dbg dir for relative paths).
- LspTest is 20 goldens now (ProjectDiscovery, MagellanicConfig,
  MagellanicConfigPriority added; all byte-exact ESCAPE).

## Session 5 (2026-08-31, same branch): json auto-impl fixed, LspProtocol on #impl_json_serializable

- **MetaJsonVectorOfSerializable ROOT CAUSE was NOT the interface-registration
  ordering the sentinel suspected**: a HAND-EXPANDED repro (no meta at all)
  failed identically. Real chain: `Vector<Kid>` specialization
  (`at() -> Option<T>`) instantiates `Option<Kid>` → the `#specializing
  __builtin.Option<T>` block INJECTS an impl that binds in the SPECIALIZED
  type's scope (std/builtin namespaces) → the spliced
  `#json_read_expr_ast(T,...)` referenced the element by its CONCRETE short
  name (`Kid.json_deserialize(...)`), unresolvable there (file _ns_
  namespaces are invisible cross-file AND not addressable symbols —
  add_or_merge_namespace adds a scope child, no namespace_sym). Fix:
  json_read_expr_ast takes the element SPELLING; the Option/Box blocks pass
  "T", which the specialized scope binds to the concrete type
  (inject_specializing_impl's documented contract). Why json.penguin itself
  never tripped this: JsonValue has no IJsonSerializable impl, so
  Option<JsonValue> never gets the injected impl.
- **Three more auto-impl gaps fixed the same day** (found via LSP-shape
  probes): (1) display-name SUBSTRING dispatch misrouted
  HashMap<string,Vector<X>> to the Vector branch — dispatch on the base name
  (segment before the first '<') now; (2) self-referential fields
  (Vector<Self>) were silently SKIPPED — the class's own impl isn't spliced
  into the AST yet when its #fun expands; the serializable check now also
  accepts compiler().get_current_scope()'s class; (3) container-valued
  HashMap entries / container elements spliced nonexistent
  `Vector.json_deserialize` — the read path recurses
  json_read_fill_stmt/json_read_push_stmt with depth-suffixed temps, and
  json_type_spelling no longer appends generic args twice (display_name
  already includes them; base = def name now). Locked by
  Tests/StdlibTest/MetaJsonRecursiveNestedContainers.md.
- **LspProtocol.penguin is on #impl_json_serializable now** (all 12 structs,
  hand-written serializers deleted; declaration order == wire order so all 20
  LspTest goldens stay byte-exact). This is the FIRST meta-JIT use in a lib
  consumer: json.penguin's #funs arrive as embedded per-file SourceInputs
  from libemperorpenguin.penguin-lib — remember the lib must be REBUILT
  (`make build/libemperorpenguin.penguin-lib`, ~20 min) after ANY
  std/penguin/json.penguin change before `make lsp` picks it up (the lib
  embeds the source verbatim).
- Known remaining auto-impl gap (pre-existing, untouched): Option/Box FIELDS
  in an auto-impl'd class die with E_INTERNAL "Symbol register not found for:
  <global>.__builtin" (probe build/repro/p3.penguin). LSP structs don't use
  them; MetaJsonOptionBox (locals) is green.

## 2026-09-13 分诊：LSP didOpen 挂死（全矩阵 prebuilt 17 ERROR 的根因）——已解决，见下节

`build/lsp`（9/11 22:19 构建，lib 配套）在 **didOpen**（语义分析路径）后死等：
单线程阻塞在 `rt_sigsuspend`（/proc wchan），stdin EOF 也不退出；init/shutdown/exit 不带
didOpen 的会话正常 exit 0。时间强吻合 8e7cb559 "implement generation based gc"
（2026-09-11 01:44，gc.c +3337 行 / scheduler.c +81 行）——build/lsp 构建于该提交之后，
所以挂死自 generation GC 起就在。9/12 门禁只跑 babypenguin,pass2,pass3（无 prebuilt），
从未暴露；2026-09-13 全量矩阵（首次含 prebuilt）暴露为 LspTest 17×"run timed out after 60s"。

## 2026-09-13 解决：不是 GC 信号，是旧编译器工件 × 新 stdlib 的 pass-3 失控（活循环）

上一节的 rt_sigsuspend/生成GC 归因是**误诊**。gdb 抓栈（yama ptrace_scope=1 不能 attach，
要用 gdb 启动为目标进程的父进程 + FIFO 保住 stdin + kill -INT 目标 pid 让 batch gdb 停机
取栈）三次采样全部停在：

```
MonomorphizePass.run → collect_generic_instantiations → collect_instantiations_from_def /
collect_from_ast_statement_safe → try_add_instantiation → BoundType_is_same_type ↔
matches_specialization_of ↔ is_template_args_of （99% CPU，state RN）
```

根因链：**build/lsp 与 build/libemperorpenguin.penguin-lib 是 9/11 22:19 的（嵌入
pre-T6 守卫的编译器），而 LSP 在运行时从 cwd 读磁盘上的 stdlib**——HEAD 的
core_builtin.penguin 已带 T5 IGenerator + T6 MapIterator/FilterIterator 方法级模板组合子，
旧编译器的 pass-3 特化收集撞上就指数失控（正是 c98665e4 守卫修复的那族 OOM bug）。
wchan=rt_sigsuspend 是红鲱鱼：running 任务的 wchan 显示陈旧等待通道，`ps` 的 RN + 99% CPU
才是真相。修复 = `make lsp` 从 HEAD 重建（级联重发 pass3.ll/pass3——9/13 bootstrap 收尾时
pass2 链接(03:12)比 pass3.ll(03:11)晚了几秒的 mtime 倒挂，无害）；重建后最小 didOpen 会话
0.28s exit 0，LspTest 23/23 绿。

- **结构性陷阱（记住）**：prebuilt LSP 与**检出态 stdlib** 耦合，不是与构建时 stdlib 耦合。
  stdlib 语义面变化后不 `make lsp` 就跑 Prebuilt LspTest = 拿旧编译器编译新 stdlib，
  runner 无法感知这种陈旧。改 stdlib/编译器源后记得 `make lsp`。
- Completion.md golden 合法漂移并已更新（347→358 符号：map/filter/reduce/all/any/into、
  MapIterator(src,f)/FilterIterator(pred)、IGenerator；`src` 因按名去重换到更早的
  MapIterator 块）——用 result.json 的 actualStdout 反转义回写，roundtrip 校验过。
- 23 个 LspTest md 即红哨兵（期望 exit 0），修复后自动转绿，无需新增测试。
- 与 2026-09-13 的三项编译器修复（lower_new / wait-port / emit_call_indirect）无关——
  那三个已随当晚 bootstrap 进入 pass2/pass3。
