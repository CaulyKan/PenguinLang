# DocumentSymbol
## Description
textDocument/documentSymbol e2e against the prebuilt LSP server: the document's namespace member tree comes back as DocumentSymbol[] — class Greeter (kind 5) with field name (8) and method greet (6), enum Color (10) with members Red/Green (22) at their own declaration sites (enum member symbols carry real locations since the parser/binder started populating them — variants used to resolve to the enum's location), free function hello (12). The user namespace node itself is spliced away (EP bound namespaces carry no location); stdlib defs are filtered by location filename. Byte-exact via the ESCAPE mode (LSP frame headers contain CR bytes). Server built by 'make lsp'.

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
Stdin: `Content-Length: 58\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}Content-Length: 52\r\n\r\n{"jsonrpc":"2.0","method":"initialized","params":{}}Content-Length: 431\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin","languageId":"penguin","version":1,"text":"namespace demo {\\n    class Greeter {\\n        name : string = \\"\\";\\n        fun greet(this) -> string {\\n            return \\"hi \\" + this.name;\\n        }\\n    }\\n    enum Color { Red; Green; }\\n    fun hello() {\\n        let g : mut Greeter = new Greeter();\\n    }\\n}\\n"}}}Content-Length: 127\r\n\r\n{"jsonrpc":"2.0","id":2,"method":"textDocument/documentSymbol","params":{"textDocument":{"uri":"file:///tmp/lsp_doc.penguin"}}}Content-Length: 44\r\n\r\n{"jsonrpc":"2.0","id":3,"method":"shutdown"}Content-Length: 33\r\n\r\n{"jsonrpc":"2.0","method":"exit"}`
ExpectedExitCode: 0
ExpectedStdout: ESCAPE `Content-Length: 331\r\n\r\n{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"completionProvider":{"triggerCharacters":[".",":"]},"documentSymbolProvider":true,"definitionProvider":true,"referencesProvider":true,"hoverProvider":true,"inlayHintProvider":true,"renameProvider":{"prepareProvider":false},"documentFormattingProvider":true}}}Content-Length: 124\r\n\r\n{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{"uri":"file:///tmp/lsp_doc.penguin","diagnostics":[]}}Content-Length: 1437\r\n\r\n{"jsonrpc":"2.0","id":2,"result":[{"name":"Greeter","kind":5,"range":{"start":{"line":1,"character":10},"end":{"line":1,"character":17}},"selectionRange":{"start":{"line":1,"character":10},"end":{"line":1,"character":17}},"children":[{"name":"name","kind":8,"range":{"start":{"line":2,"character":8},"end":{"line":2,"character":12}},"selectionRange":{"start":{"line":2,"character":8},"end":{"line":2,"character":12}},"children":[]},{"name":"greet","kind":6,"range":{"start":{"line":3,"character":12},"end":{"line":3,"character":17}},"selectionRange":{"start":{"line":3,"character":12},"end":{"line":3,"character":17}},"children":[]}]},{"name":"Color","kind":10,"range":{"start":{"line":7,"character":9},"end":{"line":7,"character":14}},"selectionRange":{"start":{"line":7,"character":9},"end":{"line":7,"character":14}},"children":[{"name":"Red","kind":22,"range":{"start":{"line":7,"character":17},"end":{"line":7,"character":20}},"selectionRange":{"start":{"line":7,"character":17},"end":{"line":7,"character":20}},"children":[]},{"name":"Green","kind":22,"range":{"start":{"line":7,"character":22},"end":{"line":7,"character":27}},"selectionRange":{"start":{"line":7,"character":22},"end":{"line":7,"character":27}},"children":[]}]},{"name":"hello","kind":12,"range":{"start":{"line":8,"character":8},"end":{"line":8,"character":13}},"selectionRange":{"start":{"line":8,"character":8},"end":{"line":8,"character":13}},"children":[]}]}Content-Length: 38\r\n\r\n{"jsonrpc":"2.0","id":3,"result":null}`
ExpectedStderr: DISCARD
