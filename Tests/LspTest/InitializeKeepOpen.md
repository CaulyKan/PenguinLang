# InitializeKeepOpen
## Description
The vscode-shape regression lock for the fd-wake/pipeline scheduling stall: the client sends initialize and WAITS for the reply before sending anything else, so stdin stays OPEN for the whole session (StdinClose: false) and termination is driven by the exit frame, never by stdin EOF. Before the fix, the scheduler blocked in poll() after processing the fd wake while the response pipeline (parser → main → serializer → writer) was still queued — the reply only flushed on the NEXT stdin event, deadlocking the handshake. The closed-stdin form of every other e2e test can never exercise this path.

## Apply To
* Prebuilt

## Test Code
```
// Session shape only — the real payload lives in Stdin below. The server
// binary is named in Run Args (built by make lsp).
```

## Run LSP
Args: `build/linux/penguin-lsp`
Stdin: `Content-Length: 107\n\n{"jsonrpc":"2.0","id":7,"method":"initialize","params":{"processId":null,"rootUri":null,"capabilities":{}}}Content-Length: 44\n\n{"jsonrpc":"2.0","id":8,"method":"shutdown"}Content-Length: 33\n\n{"jsonrpc":"2.0","method":"exit"}`
StdinClose: false
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":7,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":8,"result":null}`
ExpectedStderr: DISCARD
