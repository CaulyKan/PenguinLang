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
