# MagellanicConfig
## Description
Workspace-config routing e2e against the prebuilt LSP server: initialize carries rootUri = Tests/LspTest/fixtures/lspcfg; the server reads .magellanic.config AT THE WORKSPACE ROOT (array form: projects[0] routes dir "sub" -> sub/configproj.penguins). didOpen sub/cfgmain.penguin compiles the ROUTED project (cfgmain + cfglib): (1) clean diagnostics, (2) textDocument/definition on `cfglib.cfg_target_fn` (line 1 char 25) resolves CROSS-FILE into cfglib.penguin, (3) documentSymbol lists only this document's symbols. ${PENGUIN_ROOT} expands in Stdin/ExpectedStdout; the runner recomputes the Stdin frame counts after expansion, and ExpectedStdout wildcards every count as MATCH \d+. Bodies byte-exact via the MATCH mode (Content-Length counts wildcarded as \d+). Server built by 'make lsp'.

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
Stdin: `Content-Length: 140\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg"}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 298\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg/sub/cfgmain.penguin","languageId":"penguin","version":1,"text":"fun main() {\\n    let v: i64 = cfglib.cfg_target_fn(1);\\n    println(v);\\n}\\n"}}}Content-Length: 223\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/definition","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg/sub/cfgmain.penguin"},"position":{"line":1,"character":25}}}Content-Length: 190\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg/sub/cfgmain.penguin"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":4,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: MATCH `Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg/sub/cfgmain.penguin","diagnostics":[]}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg/sub/cfglib.penguin","range":{"start":{"line":2,"character":4},"end":{"line":2,"character":17}}}]}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":3,"result":[{"name":"main","kind":12,"range":{"start":{"line":0,"character":4},"end":{"line":0,"character":8}},"selectionRange":{"start":{"line":0,"character":4},"end":{"line":0,"character":8}},"children":[]}]}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":4,"result":null}`
ExpectedStderr: DISCARD
