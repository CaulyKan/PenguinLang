# ParseDiagnostic
## Description
Parser-diagnostic e2e against the prebuilt LSP server: `let x = ;` used to recover SILENTLY (the primary-expression fallback returned an empty-string constant — empty LSP diagnostics, 'expected value token' only at clang time). The parser now reports missing-initializer-after-'=' at the three declaration sites, folds the error with its real file:line:col (parse_error_location lifts the prefix), and publishDiagnostics carries it at the document's 2:13. Bodies byte-exact via the MATCH mode (Content-Length counts wildcarded as \d+). Server built by 'make lsp'.

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 187\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"fun f() {\\n    let x = ;\\n}\\n"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: MATCH `Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[{"range":{"start":{"line":1,"character":12},"end":{"line":1,"character":13}},"severity":1,"message":"/tmp/lsp_doc.penguin:2:13: missing initializer after '='","source":"penguinlang"}]}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":2,"result":null}`
ExpectedStderr: DISCARD
