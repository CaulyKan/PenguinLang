# Hover
## Description
textDocument/hover e2e against the prebuilt LSP server: (1) hovering `Point` (a class) returns a markdown code block with `class Point`; (2) hovering `get_x` (a method) returns its bound signature `fun get_x(this) -> i64` built from the bound function symbol (parameters with types, `this` receivers skipped). Signatures come from the last error-free compile's symbol index. Byte-exact via ESCAPE. Server built by 'make lsp'.

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 361\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"class Point {\\n    x : i64 = 0;\\n    fun get_x(this) -> i64 {\\n        return this.x;\\n    }\\n}\\nfun main() -> i64 {\\n    let p : mut Point = new Point();\\n    println(\\"x\\");\\n    return p.get_x();\\n}\\n"}}}Content-Length: 155\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"position":{"line":7,"character":18}}}Content-Length: 155\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"position":{"line":2,"character":10}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":4,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 124\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[]}}Content-Length: 180\r\n\r\n{"jsonrpc":"2.0","id":2,"result":{"contents":{"kind":"markdown","value":"```penguin\\nclass Point\\n```"},"range":{"start":{"line":0,"character":6},"end":{"line":0,"character":11}}}}Content-Length: 187\r\n\r\n{"jsonrpc":"2.0","id":3,"result":{"contents":{"kind":"markdown","value":"```penguin\\nfun get_x() -> i64\\n```"},"range":{"start":{"line":2,"character":8},"end":{"line":2,"character":13}}}}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":4,"result":null}`
ExpectedStderr: DISCARD
