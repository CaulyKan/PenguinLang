# CrashSurvival
## Description
Server-survival e2e against the prebuilt LSP server: didOpen of an unparseable document (`fun broken( {` — truncated parameter list) whose parse used to KILL the server (TokenStream.advance overran the token stream and called exit(1); recompile() had no try/catch). Now the overrun raises a catchable RuntimeError, recompile()'s try/catch publishes an 'internal compiler error' diagnostic (plus the folded parse errors), last_ok stays at the previous error-free unit, and the SUBSEQUENT documentSymbol request still answers (empty array — no error-free compile of this doc), shutdown/exit complete cleanly with exit code 0. Locks the sjlj-through-embedded-compile path. Bodies byte-exact via the MATCH mode (Content-Length counts wildcarded as \d+). Server built by 'make lsp'.

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 173\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"fun broken( {\\n"}}}Content-Length: 127\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: MATCH `Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"severity":1,"message":"internal compiler error: Invalid advance of token stream at end of input /tmp/lsp_doc.penguin:2:1","source":"penguinlang"}]}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[]}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
