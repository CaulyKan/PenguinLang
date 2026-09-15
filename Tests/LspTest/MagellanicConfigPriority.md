# MagellanicConfigPriority
## Description
Config-over-.penguins priority e2e: sub/decoy.penguins sits IN THE SAME DIRECTORY as the opened document (the upward .penguins search would find it FIRST and compile only broken decoymain.penguin), but the config hit wins — hover on `cfglib.cfg_target_fn` (line 1 char 25) returns the CROSS-FILE signature from cfglib.penguin (result null / empty would mean the decoy won). initialize carries rootUri = Tests/LspTest/fixtures/lspcfg; the server reads .magellanic.config AT THE WORKSPACE ROOT (array form: projects[0] routes dir "sub" -> sub/configproj.penguins). didOpen sub/cfgmain.penguin compiles the ROUTED project (cfgmain + cfglib). ${PENGUIN_ROOT} expands in Stdin/ExpectedStdout; the runner recomputes the Stdin frame counts after expansion, and ExpectedStdout wildcards every count as MATCH \d+. Bodies byte-exact via the MATCH mode (Content-Length counts wildcarded as \d+). Server built by 'make lsp'.

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
Stdin: `Content-Length: 140\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg"}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 298\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg/sub/cfgmain.penguin","languageId":"penguin","version":1,"text":"fun main() {\\n    let v: i64 = cfglib.cfg_target_fn(1);\\n    println(v);\\n}\\n"}}}Content-Length: 218\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/hover","params":{"textDocument":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg/sub/cfgmain.penguin"},"position":{"line":1,"character":25}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: MATCH `Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file://${PENGUIN_ROOT}/Tests/LspTest/fixtures/lspcfg/sub/cfgmain.penguin","diagnostics":[]}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":2,"result":{"contents":{"kind":"markdown","value":"```penguin\\nfun cfg_target_fn(x: i64) -> i64\\n```"},"range":{"start":{"line":2,"character":4},"end":{"line":2,"character":17}}}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
