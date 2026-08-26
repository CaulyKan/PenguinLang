# SessionLifecycle
## Description
Full LSP session lifecycle e2e against the prebuilt PenguinLang-native LSP server (tmp/lsp — MagellanicPenguin/LspServer compiled by pass3; see .agents/plans/emperorpenguin-lsp-ports.md). initialize → capabilities response, initialized notification, didOpen of a document with a semantic error (no_such_fn) → publishDiagnostics carrying the E_RESOLVE_SYMBOL message, shutdown → null result, exit → clean process exit 0 after every queued frame reached the wire (the exit control rides the outbound hub in frame order). Byte-exact stdout locks the whole pipeline: StdioStream fd parking → JsonInputParser framing/demux → LspMain routing → embedded EmperorPenguinCompiler recompile (stdlib discovery via the exe-dir upward walk) → JsonOutputParser serialization. This session is also the byte-exact regression lock for the GC bug where an unaligned parked sp (char-marker capture) made the conservative stack scan read past a coroutine stack region's mmap end — pre-fix this session died with SIGSEGV on the publishDiagnostics allocation. Stdin frames use the framer's lenient \n\n terminator (the runner's Stdin escapes cannot carry CR); ExpectedStdout uses the ESCAPE mode to assert the strict \r\n\r\n framing byte-exactly. The server exe is built by './penguin -lsp' (rebuild after compiler changes; this test only re-copies it).

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 191\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"fun f() {\\n    no_such_fn();\\n}\\n"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 282\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"severity":1,"message":"Cannot resolve symbol 'no_such_fn'","source":"penguinlang"}]}}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":2,"result":null}`
ExpectedStderr: DISCARD
