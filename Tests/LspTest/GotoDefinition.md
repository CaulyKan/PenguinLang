# GotoDefinition
## Description
textDocument/definition e2e against the prebuilt LSP server: two lookups in one session. (1) The identifier under the cursor is `Point` in `new Point()` (line 7 char 18) → same-file Location of the class declaration (line 0 char 6). (2) `println` (line 8 char 7) → cross-file Location of the extern declaration in the injected core_builtin.penguin stdlib — locks the whole-program symbol index path with same-file preference. v1 semantics: re-lex + name lookup, no scope resolution (C#-server parity). Byte-exact via ESCAPE. Server built by 'make lsp'.

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 361\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"class Point {\\n    x : i64 = 0;\\n    fun get_x(this) -> i64 {\\n        return this.x;\\n    }\\n}\\nfun main() -> i64 {\\n    let p : mut Point = new Point();\\n    println(\\"x\\");\\n    return p.get_x();\\n}\\n"}}}Content-Length: 160\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/definition","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"position":{"line":7,"character":18}}}Content-Length: 159\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"textDocument/definition","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"position":{"line":8,"character":7}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":4,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 124\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[]}}Content-Length: 148\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"uri":"file:///tmp/lsp_doc.penguin","range":{"start":{"line":0,"character":6},"end":{"line":0,"character":11}}}]}Content-Length: 149\r\n\r\n{"jsonrpc":"2.0","id":3,"result":[{"uri":"file://core_builtin.penguin","range":{"start":{"line":3,"character":15},"end":{"line":3,"character":22}}}]}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":4,"result":null}`
ExpectedStderr: DISCARD
