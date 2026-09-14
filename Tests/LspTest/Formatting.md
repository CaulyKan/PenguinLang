# Formatting
## Description
textDocument/formatting e2e against the prebuilt LSP server: whole-document lexically-faithful formatting as a single full-range TextEdit — tokens re-emitted with normalized spacing (`s=a+b` becomes `s = a + b`), 4-space indent per brace depth (the over-indented println re-indents), one statement per line, and the line comment `// keep me` PRESERVED in source order (comments are pre-scanned from the raw text because the lexer drops them). No re-parsing of semantics; the formatted text must lex to the same token stream. Byte-exact via ESCAPE. Server built by 'make lsp'.

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 320\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"namespace   fmt {\\nfun add(a: i64, b: i64) -> i64 {\\n    let s=a+b;  // keep me\\n    return s;\\n}\\n}\\ninitial {\\n        println(cast<string>(fmt.add(1,2)));\\n}\\n"}}}Content-Length: 167\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/formatting","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"},"options":{"tabSize":4,"insertSpaces":true}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 124\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[]}}Content-Length: 311\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"range":{"start":{"line":0,"character":0},"end":{"line":9,"character":0}},"newText":"namespace fmt {\\n    fun add(a : i64, b : i64) -> i64 {\\n        let s = a + b;\\n        // keep me\\n        return s;\\n    }\\n}\\ninitial {\\n    println(cast<string>(fmt.add(1, 2)));\\n}\\n"}]}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
