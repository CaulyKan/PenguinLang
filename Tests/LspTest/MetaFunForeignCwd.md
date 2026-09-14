# MetaFunForeignCwd
## Description
Same #fun-document session as MetaFunDocumentSurvives, but the server process runs with Cwd: ${WORKDIR} (the per-combo workdir — a FOREIGN cwd with no EmperorPenguin tree). MetaEngine unit B's base sources (utils/bound/ast layers) used to be read cwd-relative, so from any non-repo cwd the unit-B compile failed with unresolved BoundType/Expression (historically exit(1) — the server died; now a catchable throw) and the JIT never ran. read_compiler_source resolves cwd-first then exe-relative/upward, so the repo tree beside build/lsp keeps unit B working. Asserts identical frames to the repo-cwd run: clean diagnostics + documentSymbol `main` + exit 0.
A document containing a `#fun` meta function and a `#dbl(21)` meta call exercises the embedded compiler's MetaEngine JIT inside the LSP server process. The linux LSP exe used to be linked without -enable-meta: `penguin_jit_create` returned the no-JIT stub and MetaEngine.init called exit(1) — the server DIED ~7s after didOpen (broken pipe, no diagnostics, no documentSymbol response, exit 1) whenever any document used #fun-based meta (the LSP's own sources via #impl_json_serializable, or user code). Now the exe carries the ORC JIT (make lsp links -enable-meta; the .so's jit refs bind from the exe via -rdynamic), and even on a no-JIT build MetaEngine.init throws a catchable error instead of exit(1) so the server degrades to a diagnostic and survives. Asserts: clean diagnostics, documentSymbol answers `main` (the #fun itself yields no symbol), shutdown/exit 0.

## Apply To
* Prebuilt

## Test Code
```
// The server executable itself — Run Args below names it; this block is
// documentation only for the Prebuilt backend.
```

## Run LSP
Args: `build/linux/penguin-lsp`
Env: ``
Cwd: `${WORKDIR}`
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 272\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/metafun_doc.penguin","languageId":"penguin","version":1,"text":"#fun dbl(x: i64) -> i64 { return x * 2; }\\nfun main() -> i64 {\\n    let r: i64 = #dbl(21);\\n    return r;\\n}\\n"}}}Content-Length: 131\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/metafun_doc.penguin"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 128\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/metafun_doc.penguin","diagnostics":[]}}Content-Length: 232\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"name":"main","kind":12,"range":{"start":{"line":1,"character":4},"end":{"line":1,"character":8}},"selectionRange":{"start":{"line":1,"character":4},"end":{"line":1,"character":8}},"children":[]}]}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
