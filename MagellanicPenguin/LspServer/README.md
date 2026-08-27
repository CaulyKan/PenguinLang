# PenguinLang-native LSP Server

A rewrite of the C# language server (`MagellanicPenguin/LSP/LSP.cs`) in PenguinLang itself,
built on the ports/channels/coroutine runtime (`Documentation/11_PortsChannelsEvents.md`)
and embedding the whole EmperorPenguin compiler as its analysis engine. Plan:
`.agents/plans/emperorpenguin-lsp-ports.md`.

```
                 stdin (fd 0)                       stdout (fd 1)
                     │                                   ▲
             ┌───────▼────────┐                  ┌───────┴────────┐
             │  StdioStream   │                  │  StdioStream   │
             │ reader initial │                  │ writer initial │
             │ fd_wait_read   │                  │ POLLOUT park + │
             │  rx : out port │                  │ exit sentinel  │
             └───────┬────────┘                  └───────▲────────┘
                     │ wire                              │ wire (frames)
             ┌───────▼────────┐  session   ┌────────┐    │
             │                │───────────►│LspMain │    │
             │ JsonInputParser│  docs      │ units  │    │
             │  framer + demux│───────────►│ HashMap│    │
             └────────────────┘            └───┬────┘    │
                                             Fifo<LspMessage> (per unit)
                                             ┌───▼─────────────┐
                     ┌───────────────────────►│ LspCompilation- │
                     │  out_sink: ISink view  │ Unit (per doc)  │
                     │  of JsonOutputParser's │ embedded compile │
                     │  shared hub Fifo       │ diagnostics      │
             ┌───────┴────────┐               │ queries (LspQuery│
             │ JsonOutputParser│◄─────────────┘  )             │
             │ hub Fifo (N:1)  │  LspOutMsg   └────────────────┘
             │ frame serialize │
             └────────────────┘
```

## Modules

| File | Role |
| --- | --- |
| `LspTypes.penguin` | LspMessage/LspResponse/LspError/LspNotification + `LspOutMsg` enum, JSON-RPC error codes |
| `LspFraming.penguin` | Pure Content-Length frame reassembly (`Framer`); unit-tested standalone |
| `LspJson.penguin` | JSON plumbing: message parsing, response/notification serialization, initialize capabilities, uri helpers |
| `StdioStream.penguin` | stdio boundary: reader parks the scheduler's fd integration on stdin, writer parks on stdout writability (end-to-end backpressure); the exit control rides the frame stream so every queued frame is flushed before `exit(code)` |
| `JsonInputParser.penguin` | chunks → frames (FIFO `pending`) → demux onto `session`/`docs` output ports (one frame per delta round) |
| `LspMain.penguin` | session commands (initialize/shutdown/exit) + document routing; didOpen dynamically instantiates LspCompilationUnit (module-as-actor) |
| `LspCompilationUnit.penguin` | per-document actor: full recompile on didOpen/didChange (embedded `EmperorPenguinCompiler.compile_sources`, stdlib + enable_coroutine), publishes diagnostics, serves queries against the last error-free unit |
| `LspQuery.penguin` | symbol index over `BoundCompilationUnit.definitions`; documentSymbol / definition / completion |
| `JsonOutputParser.penguin` | the outbound fan-in: shared hub Fifo (`ISink` views handed to producers), serializes each `LspOutMsg` into a JSON-RPC frame |
| `LspWiring.penguin` | top-level `construct` wiring the static half + the parking `initial` that stands in for `main` |

## Build

```sh
./penguin -b      # bootstrap tmp/pass3 first (one-time per compiler change)
./penguin -lsp    # stage 1: tmp/pass3 EmperorPenguin/EmperorPenguinLib.penguins -o tmp/libemperorpenguin.penguin-lib
                  # stage 2: tmp/pass3 --enable-coroutine LspServer.penguins --lib tmp/libemperorpenguin.penguin-lib -o tmp/lsp
```

The server links the compiler as a shared library: `tmp/lsp` contains only the 10 LSP
modules (~0.8 MB) and calls into `libemperorpenguin.penguin-lib` (~14 MB, built from
`EmperorPenguinLib.penguins`) for all compiler work — `SONAME libemperorpenguin.penguin-lib`
+ `rpath $ORIGIN`, so the exe + lib pair in `tmp/` is relocatable and `./penguin -p` copies
both into `server/linux/`. Both stages have content-addressed caches (keyed on pass3 + the
respective source sets; the lsp key includes the lib artifact). The test
runner's **Prebuilt** backend runs the exe without recompiling: `Tests/LspTest/SessionLifecycle.md`
feeds a full JSON-RPC session on stdin and asserts byte-exact frames + exit code
(`Apply To: Prebuilt`, `Compile.Args: tmp/lsp`).

## Design notes

- **Port payloads are strings only** — a deliberate architecture choice (kept after the
  compiler fix landed: `Tests/PortTest/PortPayloadEnumChannelCycle.md` is green, so rich
  port payloads are now POSSIBLE, but the explicit-Fifo wiring already works and channels
  are not legal connect sinks in EP v1, so the demux hands producers constructor-injected
  hub views either way). Rich datatypes (`LspMessage`, `LspOutMsg`) travel through explicit
  `Fifo` channels spelled as **class field types**, which the pass-3 monomorphize fixpoint
  collects eagerly.
- **Channels are not legal connect sinks** (deviation F in the design doc) — the outbound
  N:1 fan-in is a shared hub Fifo whose `ISink` views are handed to producers through
  constructors; per-document units likewise receive their inbound `cmds` Fifo by construction.
- **Exit ordering**: `exit` writes an `out_exit` control into the hub; channel order
  guarantees every frame queued ahead of it reaches the wire before StdioStream calls
  `exit(0|1)` (LSP spec: 0 after `shutdown`, 1 otherwise). stdin EOF without `exit` drains
  in-flight work and ends at quiescence with code 0.
- **Backpressure is structural**: a client that stops reading fills the stdout pipe → the
  writer parks on writability → the hub fills → producers park → the framer's pending Fifo
  fills → the reader parks → the kernel throttles the client. No flow-control code anywhere.
- **stdlib discovery** for embedded document compiles (`load_stdlib_text`): cwd first
  (repo-root runs), then an upward walk from the exe dir — so the server works from the
  test runner's per-combo workdirs and from `vscode/server/linux/` with the bundled
  `EmperorPenguin/std/penguin` tree (`./penguin -p` stages both).
- **Query semantics are v1-deliberately naive** (same as the C# server): no scope
  resolution — definition/hover look up the identifier under the cursor in the
  whole-program symbol index (same-file match preferred); completion returns keywords +
  every indexed symbol deduplicated by name. Enum member symbols carry their declaration
  locations since the bound tree started populating them (goto-def lands on the variant).
- **references / rename** are name-level within the requesting document (occurrences by
  re-lexing; declarations once, honoring `context.includeDeclaration`); rename returns a
  single-document `WorkspaceEdit`. Cross-file references need other documents' texts —
  only opened units have them in v1.
- **hover** shows the symbol's bound signature (`fun name(a: T) -> R` / `name: T` / kind
  line) from the last error-free compile.
- **inlay hints** walk the last error-free unit's bound bodies (functions, methods,
  constructors, initial blocks, nested if/while/lambda blocks) and place `: T` labels
  after `let`-declared names inside the requested range.
- **formatting** is lexically faithful whole-document: it re-emits the token stream with
  normalized spacing/indentation (4 spaces per brace depth, newline after statement
  semicolons/braces, blank-line preservation capped at one) and PRESERVES comments
  (pre-scanned from the raw text — the lexer drops them) in source order.
- **Server survives broken documents**: `recompile()` wraps the embedded compile in
  try/catch (native sjlj); a compiler panic publishes an "internal compiler error"
  diagnostic and keeps the last error-free unit, and `TokenStream.advance`'s end-of-input
  overrun raises a catchable error instead of `exit(1)`.
- **Build caching**: `./penguin -lsp` keys two content-addressed caches (the compiler
  lib and the LSP exe) on their resolved source sets + stdlib + pass3 — unchanged
  inputs rebuild in seconds. (The earlier dyn-lib deferral was reversed: the lib consumer
  surface the LSP needs — enum dispatch on `BoundDefinition`, `List` field reads, method
  calls into the `.so`, new-instance monomorphization from embedded templates — all work,
  locked in by the LspTest e2e suite.)

## vscode

The extension client (`vscode/client/src/extension.ts`) starts `PENGUINLANG_LSPSERVER_PATH`
if set, else `server/<platform>/MagellanicPenguinLSP(.exe)`. `./penguin -p` copies
`tmp/lsp` there (linux) together with the bundled stdlib; win32 keeps the C# server until
the native runtime's Windows fd integration lands.
