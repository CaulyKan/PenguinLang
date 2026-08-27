# References
## Description
textDocument/references e2e against the prebuilt LSP server: name-level occurrence scan of the requesting document. For `Point` with includeDeclaration=true the declaration (line 0) and every use (let annotation, new expression) come back; with includeDeclaration=false the declaration is dropped. v1 scans the current document only (cross-file needs other documents' texts). Byte-exact via ESCAPE. Server built by 'make lsp'.

## Apply To
* Prebuilt

## Test Code
```
// The server executable itself — Compile.Args below names it; this block is
// documentation only for the Prebuilt backend.
```

## Run LSP
Args: `build/lsp`
Env: ``
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 361\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"class Point {\\n    x : i64 = 0;\\n    fun get_x(this) -> i64 {\\n        return this.x;\\n    }\\n}\\nfun main() -> i64 {\\n    let p : mut Point = new Point();\\n    println(\\"x\\");\\n    return p.get_x();\\n}\\n"}}}Content-Length: 198\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/references","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"position":{"line":7,"character":18},"context":{"includeDeclaration":true}}}Content-Length: 199\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"textDocument/references","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"position":{"line":7,"character":18},"context":{"includeDeclaration":false}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":4,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 124\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[]}}Content-Length: 376\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"uri":"file:///tmp/lsp_doc.penguin","range":{"start":{"line":0,"character":6},"end":{"line":0,"character":11}}},{"uri":"file:///tmp/lsp_doc.penguin","range":{"start":{"line":7,"character":16},"end":{"line":7,"character":21}}},{"uri":"file:///tmp/lsp_doc.penguin","range":{"start":{"line":7,"character":28},"end":{"line":7,"character":33}}}]}Content-Length: 263\r\n\r\n{"jsonrpc":"2.0","id":3,"result":[{"uri":"file:///tmp/lsp_doc.penguin","range":{"start":{"line":7,"character":16},"end":{"line":7,"character":21}}},{"uri":"file:///tmp/lsp_doc.penguin","range":{"start":{"line":7,"character":28},"end":{"line":7,"character":33}}}]}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":4,"result":null}`
ExpectedStderr: DISCARD
