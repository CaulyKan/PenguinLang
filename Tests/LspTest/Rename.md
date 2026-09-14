# Rename
## Description
textDocument/rename e2e against the prebuilt LSP server: renaming `Point` to `Vec2` returns a WorkspaceEdit whose changes cover the requesting document only, one TextEdit per name-level occurrence (declaration, type annotations, the new expression). v1 is name-level within the document (no scope-precise resolution — the C# server's parity); cross-document renames need the references index over other open documents. Byte-exact via ESCAPE. Server built by 'make lsp'.

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 361\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"class Point {\\n    x : i64 = 0;\\n    fun get_x(this) -> i64 {\\n        return this.x;\\n    }\\n}\\nfun main() -> i64 {\\n    let p : mut Point = new Point();\\n    println(\\"x\\");\\n    return p.get_x();\\n}\\n"}}}Content-Length: 173\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/rename","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"position":{"line":7,"character":18},"newName":"Vec2"}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 124\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[]}}Content-Length: 363\r\n\r\n{"jsonrpc":"2.0","id":2,"result":{"changes":{"file:///tmp/lsp_doc.penguin":[{"range":{"start":{"line":0,"character":6},"end":{"line":0,"character":11}},"newText":"Vec2"},{"range":{"start":{"line":7,"character":16},"end":{"line":7,"character":21}},"newText":"Vec2"},{"range":{"start":{"line":7,"character":28},"end":{"line":7,"character":33}},"newText":"Vec2"}]}}}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
