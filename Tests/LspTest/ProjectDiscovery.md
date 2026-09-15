# ProjectDiscovery
## Description
Project-file discovery e2e against the prebuilt LSP server: didOpen of a real on-disk document (Tests/LspTest/fixtures/lspproj/main.penguin) walks up from the document dir, finds proj.penguins, and compiles the whole project (main.penguin + libx.penguin from disk — cross-file top-level symbols need the explicit `libx` namespace, EmperorPenguin file-namespace semantics). (1) publishDiagnostics is empty (project compiles clean). (2) textDocument/definition on `libx.lsp_proj_target` (line 1 char 25, the callee after the dot) resolves CROSS-FILE to libx.penguin line 2 — the whole-program symbol index over the project compile. (3) documentSymbol lists only THIS document's symbols (project siblings filtered by location filename). ${PENGUIN_ROOT} expands in both Stdin and ExpectedStdout (Content-Length headers count the EXPANDED bytes — the runner expands after unescaping, lengths were computed on the expanded form). Bodies byte-exact via the MATCH mode (Content-Length counts wildcarded as \d+). Server built by 'make lsp'.

## Apply To
* Prebuilt

## Test Code
```
// The server executable itself — Compile.Args below names it; this block is
// documentation only for the Prebuilt backend.
```

## Run LSP
Args: `build/linux/penguin-lsp`
Env: ``
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 292\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspproj/main.penguin","languageId":"penguin","version":1,"text":"fun main() {\\n    let v: i64 = libx.lsp_proj_target(1);\\n    println(v);\\n}\\n"}}}Content-Length: 217\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/definition","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspproj/main.penguin"},"position":{"line":1,"character":25}}}Content-Length: 184\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspproj/main.penguin"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":4,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: MATCH `Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspproj/main.penguin","diagnostics":[]}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspproj/libx.penguin","range":{"start":{"line":2,"character":4},"end":{"line":2,"character":19}}}]}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":3,"result":[{"name":"main","kind":12,"range":{"start":{"line":0,"character":4},"end":{"line":0,"character":8}},"selectionRange":{"start":{"line":0,"character":4},"end":{"line":0,"character":8}},"children":[]}]}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":4,"result":null}`
ExpectedStderr: DISCARD
