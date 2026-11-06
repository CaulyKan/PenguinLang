# IncrementalDidChangeDiagnostic
## Description
Batch 4.4 sentinel: the single-compile-actor LSP (LspCompileActor) answers a didChange through the IncrementalSession — the document opens clean, the edit breaks it (undefined symbol + return type mismatch), and the publishDiagnostics frame carries BOTH errors at their locations. Locks the incremental path's diagnostics against the per-document full-recompile behavior it replaced (byte-parity with the old actor's frames) and against the replay defect fixed this batch: the location-less E_RESOLVE_SYMBOL used to be replayed verbatim from the global bucket even after the offending text was fixed (the fix replays location-less entries only for pre-pipeline passes). Frames byte-exact via MATCH (Content-Length wildcarded).

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 191\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_incr.penguin","languageId":"penguin","version":1,"text":"fun f() -> i64 {\\n    return 1;\\n}\\n"}}}Content-Length: 215\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///tmp/lsp_incr.penguin","version":2},"contentChanges":[{"text":"fun f() -> i64 {\\n    return undefined_thing;\\n}\\n"}]}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: MATCH `Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_incr.penguin","diagnostics":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"severity":1,"message":"Cannot resolve symbol 'undefined_thing'","source":"penguinlang"},{"range":{"start":{"line":1,"character":4},"end":{"line":1,"character":5}},"severity":1,"message":"Return type mismatch: expected i64 but got void in f","source":"penguinlang"}]}}Content-Length: \d+\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
