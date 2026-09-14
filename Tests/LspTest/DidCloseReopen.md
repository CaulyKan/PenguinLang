# DidCloseReopen
## Description
didClose retires the document unit: reopening the same uri afterwards must create a FRESH compilation unit and answer queries from the NEW text. Regression lock for the LspMain map leak — before the fix, didClose left the retired unit in the units map, the second didOpen was ignored (unit exists) and every later request was written into the dead unit's cmds Fifo, never answered (id=3 would get no response). Here id=2 answers from text A (class Point), id=3 answers from text B (class Vector2 — only a fresh unit that recompiled the reopened text can return it). Byte-exact via ESCAPE. Server built by 'make lsp'.

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 197\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_reopen.penguin","languageId":"penguin","version":1,"text":"class Point {\\n    x : i64 = 0;\\n}\\n"}}}Content-Length: 130\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/lsp_reopen.penguin"}}}Content-Length: 117\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didClose","params":{"textDocument":{"uri":"file:///tmp/lsp_reopen.penguin"}}}Content-Length: 199\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_reopen.penguin","languageId":"penguin","version":1,"text":"class Vector2 {\\n    y : i64 = 0;\\n}\\n"}}}Content-Length: 130\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/lsp_reopen.penguin"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":4,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 127\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_reopen.penguin","diagnostics":[]}}Content-Length: 426\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"name":"Point","kind":5,"range":{"start":{"line":0,"character":6},"end":{"line":0,"character":11}},"selectionRange":{"start":{"line":0,"character":6},"end":{"line":0,"character":11}},"children":[{"name":"x","kind":8,"range":{"start":{"line":1,"character":4},"end":{"line":1,"character":5}},"selectionRange":{"start":{"line":1,"character":4},"end":{"line":1,"character":5}},"children":[]}]}]}Content-Length: 127\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_reopen.penguin","diagnostics":[]}}Content-Length: 428\r\n\r\n{"jsonrpc":"2.0","id":3,"result":[{"name":"Vector2","kind":5,"range":{"start":{"line":0,"character":6},"end":{"line":0,"character":13}},"selectionRange":{"start":{"line":0,"character":6},"end":{"line":0,"character":13}},"children":[{"name":"y","kind":8,"range":{"start":{"line":1,"character":4},"end":{"line":1,"character":5}},"selectionRange":{"start":{"line":1,"character":4},"end":{"line":1,"character":5}},"children":[]}]}]}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":4,"result":null}`
ExpectedStderr: DISCARD
