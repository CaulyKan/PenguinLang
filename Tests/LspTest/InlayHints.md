# InlayHints
## Description
textDocument/inlayHint e2e against the prebuilt LSP server: the document's `let` declarations with inferred types get `: T` labels (kind 1) placed right after the variable name — `total: i64`, `name: string` at function top level and `inner: i64` inside the nested if block (the walker recurses through bound code blocks and if/while/lambda bodies). Types come from the bound variable symbols of the last error-free compile. Byte-exact via ESCAPE. Server built by './penguin -lsp'.

## Apply To
* Prebuilt

## Test Code
```
// The server executable itself — Compile.Args below names it; this block is
// documentation only for the Prebuilt backend.
```

## Compile
Args: `tmp/lsp`
ExpectedExitCode: 0
ExpectedStdout: DISCARD
ExpectedStderr: DISCARD

## Run
Args: ``
Env: ``
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 360\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"fun compute() -> i64 {\\n    let total = 41;\\n    let name = \\"x\\";\\n    if (total < 100) {\\n        let inner = total + 1;\\n        return inner;\\n    }\\n    return 0;\\n}\\ninitial {\\n    compute();\\n}\\n"}}}Content-Length: 197\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/inlayHint","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"range":{"start":{"line":0,"character":0},"end":{"line":20,"character":0}}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 124\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[]}}Content-Length: 228\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"position":{"line":1,"character":9},"label":": i64","kind":1},{"position":{"line":2,"character":8},"label":": string","kind":1},{"position":{"line":4,"character":13},"label":": i64","kind":1}]}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
