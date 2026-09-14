# SelfHostProjectModeLibChain
## Description
The user-visible 'LSP hangs on its own sources' e2e, minimized: initialize rootUri routes through Tests/LspTest/fixtures/selfhost/.magellanic.config to a project-mode compile that consumes the REAL build/libemperorpenguin.penguin-lib (14 MB of embedded-source metadata) — the exact load path that hung forever when the deployed lib predated the O(1) JsonReader fix (read_meta's per-char string_char_at strlen'd the whole 14 MB text — CPU spin, no frame ever answered), and that then crashed the server (~7s, exit 1) once the parse was fast but the exe carried no meta JIT. probe.penguin's `#impl_json_serializable()` expands via #fun meta JIT through the lib-embedded json.penguin, so this locks in BOTH layers: lib-chain metadata parse performance (this test never returns if it regresses to O(n^2)) and the JIT-carrying LSP exe. Asserts clean diagnostics, documentSymbol (Probe + fields, main), exit 0. ~27s on the reference machine — the runtime IS the regression signal.

## Apply To
* Prebuilt

## Test Code
```
// The server executable itself — Run Args below names it; this block is
// documentation only for the Prebuilt backend.
```

## Run LSP
Args: `build/linux/penguin-lsp`
Env: ``
Stdin: `Content-Length: 142\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/selfhost"}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 419\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/selfhost/proj/probe.penguin","languageId":"penguin","version":1,"text":"class Probe {\\n    x: mut i64 = 0;\\n    label: mut string = \\"\\";\\n    #impl_json_serializable();\\n}\\n\\nfun main() -> i64 {\\n    let p: mut Probe = new Probe();\\n    p.x = 21;\\n    return p.x;\\n}\\n"}}}Content-Length: 191\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/selfhost/proj/probe.penguin"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 188\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/selfhost/proj/probe.penguin","diagnostics":[]}}Content-Length: 820\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"name":"Probe","kind":5,"range":{"start":{"line":0,"character":6},"end":{"line":0,"character":11}},"selectionRange":{"start":{"line":0,"character":6},"end":{"line":0,"character":11}},"children":[{"name":"x","kind":8,"range":{"start":{"line":1,"character":4},"end":{"line":1,"character":5}},"selectionRange":{"start":{"line":1,"character":4},"end":{"line":1,"character":5}},"children":[]},{"name":"label","kind":8,"range":{"start":{"line":2,"character":4},"end":{"line":2,"character":9}},"selectionRange":{"start":{"line":2,"character":4},"end":{"line":2,"character":9}},"children":[]}]},{"name":"main","kind":12,"range":{"start":{"line":6,"character":4},"end":{"line":6,"character":8}},"selectionRange":{"start":{"line":6,"character":4},"end":{"line":6,"character":8}},"children":[]}]}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
