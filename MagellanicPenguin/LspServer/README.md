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
./penguin -lsp    # tmp/pass3 --enable-coroutine MagellanicPenguin/LspServer/LspServer.penguins -o tmp/lsp
```

Milestone-grade build (the entire ~16k-line compiler is part of the server). The test
runner's **Prebuilt** backend runs it without recompiling: `Tests/LspTest/SessionLifecycle.md`
feeds a full JSON-RPC session on stdin and asserts byte-exact frames + exit code
(`Apply To: Prebuilt`, `Compile.Args: tmp/lsp`).

## Design notes

- **Port payloads are strings only** — output ports' injected `_Fanout<T>` defaults are only
  green for primitive payloads today (`Tests/PortTest/PortPayloadEnumChannelCycle.md` tracks
  the compiler fix). Rich datatypes (`LspMessage`, `LspOutMsg`) travel through explicit
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
  resolution — definition looks up the identifier under the cursor in the whole-program
  symbol index (same-file match preferred); completion returns keywords + every indexed
  symbol deduplicated by name. Enum variants resolve to their enum's location (member
  symbols carry no location in the bound tree yet).

## vscode

The extension client (`vscode/client/src/extension.ts`) starts `PENGUINLANG_LSPSERVER_PATH`
if set, else `server/<platform>/MagellanicPenguinLSP(.exe)`. `./penguin -p` copies
`tmp/lsp` there (linux) together with the bundled stdlib; win32 keeps the C# server until
the native runtime's Windows fd integration lands.
