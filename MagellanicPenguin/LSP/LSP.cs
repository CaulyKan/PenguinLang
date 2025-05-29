using System.Text;
using BabyPenguin;
using LanguageServer;
using LanguageServer.Client;
using LanguageServer.Infrastructure.JsonDotNet;
using LanguageServer.Json;
using LanguageServer.Parameters;
using LanguageServer.Parameters.General;
using LanguageServer.Parameters.TextDocument;
using LanguageServer.Parameters.Window;
using LanguageServer.Parameters.Workspace;
using PenguinLangSyntax;
using System.Collections.Concurrent;
using System.Threading;
using BabyPenguin.SemanticNode;
using BabyPenguin.SemanticInterface;
using BabyPenguin.Symbol;

namespace MagellanicPenguin
{
    public class App : ServiceConnection
    {
        private Uri _workerSpaceRoot;
        private int _maxNumberOfProblems = 1000;
        private TextDocumentManager _documents;

        public App(Stream input, Stream output)
            : base(input, output)
        {
            _documents = new TextDocumentManager();
            _documents.Changed += Documents_Changed;
        }

        private void Documents_Changed(object sender, TextDocumentChangedEventArgs e)
        {
            Logger.Instance.Log($"Enter Documents_Changed({sender}, {e})");
            CompileDocument(e.Document);
            Logger.Instance.Log($"Leave Documents_Changed");
        }

        protected override Result<InitializeResult, ResponseError<InitializeErrorData>> Initialize(InitializeParams @params)
        {
            Logger.Instance.Log($"Enter Initialize(rootUri={@params.rootUri})");
            _workerSpaceRoot = @params.rootUri;
            var result = new InitializeResult
            {
                capabilities = new ServerCapabilities
                {
                    textDocumentSync = TextDocumentSyncKind.Full,
                    completionProvider = new CompletionOptions
                    {
                        triggerCharacters = new[] { ".", ":", ">", "(", "[", "{", "\"", "'", "`", "@", "#", "$", "%", "&", "*", "+", "-", "=", "|", "\\", "/", "<", "?", "~", "!", "^" }
                    },
                    documentSymbolProvider = true,
                    definitionProvider = true,
                    referencesProvider = true,
                }
            };
            Logger.Instance.Log("Leave Initialize");
            return Result<InitializeResult, ResponseError<InitializeErrorData>>.Success(result);
        }

        protected override void DidOpenTextDocument(DidOpenTextDocumentParams @params)
        {
            Logger.Instance.Log($"Enter DidOpenTextDocument(uri={@params.textDocument.uri}, version={@params.textDocument.version})");
            _documents.Add(@params.textDocument);
            Logger.Instance.Log("Leave DidOpenTextDocument");
        }

        protected override void DidChangeTextDocument(DidChangeTextDocumentParams @params)
        {
            Logger.Instance.Log($"Enter DidChangeTextDocument(uri={@params.textDocument.uri}, version={@params.textDocument.version}, changes={@params.contentChanges.Length})");
            _documents.Change(@params.textDocument.uri, @params.textDocument.version, @params.contentChanges);
            Logger.Instance.Log("Leave DidChangeTextDocument");
        }

        protected override void DidCloseTextDocument(DidCloseTextDocumentParams @params)
        {
            Logger.Instance.Log($"Enter DidCloseTextDocument(uri={@params.textDocument.uri})");
            _documents.Remove(@params.textDocument.uri);
            Logger.Instance.Log("Leave DidCloseTextDocument");
        }

        protected override void DidChangeConfiguration(DidChangeConfigurationParams @params)
        {
            Logger.Instance.Log($"Enter DidChangeConfiguration(settings={@params?.settings?.languageServerExample?.maxNumberOfProblems})");
            _maxNumberOfProblems = @params?.settings?.languageServerExample?.maxNumberOfProblems ?? _maxNumberOfProblems;
            Logger.Instance.Log($"maxNumberOfProblems is set to {_maxNumberOfProblems}.");
            foreach (var document in _documents.All)
            {
                CompileDocument(document);
            }
            Logger.Instance.Log("Leave DidChangeConfiguration");
        }

        private void CompileDocument(TextDocumentItem document)
        {
            Logger.Instance.Log($"Enter CompileDocument(uri={document.uri}, version={document.version})");
            var stringWriter = new StringWriter();
            var errorReporter = new ErrorReporter(stringWriter);
            var compiler = new SemanticCompiler(errorReporter);
            compiler.AddSource(document.text, ConvertUriToPath(document.uri));

            var result = new CompilationResult();
            var diagnostics = new List<Diagnostic>();
            try
            {
                result.Model = compiler.Compile();
            }
            catch (BabyPenguinException e)
            {
                diagnostics.Add(new Diagnostic
                {
                    range = ConvertSourceLocation(e.Location),
                    message = e.Message,
                    severity = DiagnosticSeverity.Error
                });
            }
            catch (PenguinLangException e)
            {
                diagnostics.Add(new Diagnostic
                {
                    range = ConvertSourceLocation(SourceLocation.Empty()),
                    message = e.Message,
                    severity = DiagnosticSeverity.Error
                });
            }

            diagnostics.AddRange(errorReporter.Messages.Where(i => i.Level != ErrorReporter.DiagnosticLevel.Debug && i.Level != ErrorReporter.DiagnosticLevel.Info).Select(i => new Diagnostic
            {
                range = ConvertSourceLocation(i.SourceLocation),
                message = i.Message,
                severity = i.Level == ErrorReporter.DiagnosticLevel.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning
            }));

            _documents.UpdateCompilationResult(document.uri, result);

            Proxy.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                uri = document.uri,
                diagnostics = diagnostics.ToArray()
            });
            Logger.Instance.Log("Leave CompileDocument");
        }

        private static string ConvertUriToPath(Uri uri)
        {
            var temp = uri.LocalPath.Replace("/", "\\");
            if (temp.StartsWith("\\")) temp = temp.Substring(1);
            return temp;
        }

        private static LanguageServer.Parameters.Range ConvertSourceLocation(SourceLocation e)
        {
            var result = new LanguageServer.Parameters.Range
            {
                start = new Position { line = Math.Max(0, e.RowStart - 1), character = Math.Max(0, e.ColStart) },
                end = new Position { line = Math.Max(0, e.RowEnd - 1), character = Math.Max(0, e.ColEnd) }
            };
            return result;
        }

        private static Uri ConvertPathToUri(string fileName)
        {
            return new Uri("file:///" + fileName.Replace("\\", "/"));
        }

        protected override void DidChangeWatchedFiles(DidChangeWatchedFilesParams @params)
        {
            Logger.Instance.Log($"Enter DidChangeWatchedFiles(changes={@params.changes.Length})");
            Logger.Instance.Log("Leave DidChangeWatchedFiles");
        }

        protected override Result<CompletionResult, ResponseError> Completion(CompletionParams @params)
        {
            Logger.Instance.Log($"Enter Completion(textDocument={@params.textDocument.uri}, position={@params.position.line}:{@params.position.character})");

            var document = _documents.All.FirstOrDefault(d => d.uri == @params.textDocument.uri);
            if (document == null)
            {
                Logger.Instance.Log("Leave Completion: document not found");
                return Result<CompletionResult, ResponseError>.Error(new ResponseError
                {
                    code = ErrorCodes.InvalidParams,
                    message = "Document not found"
                });
            }

            var model = _documents.GetCompilationResult(document.uri).LastSuccessModel;
            if (model == null)
            {
                Logger.Instance.Log("Leave Completion: no successful compilation result is available.");
                return Result<CompletionResult, ResponseError>.Success(new CompletionResult(new CompletionItem[] { }));
            }

            // Convert position to SourceLocation
            var sourceLocation = new SourceLocation(
                ConvertUriToPath(document.uri),
                "",
                (int)@params.position.line + 1,
                (int)@params.position.line + 1,
                (int)@params.position.character,
                (int)@params.position.character
            );

            // Get current scope
            var currentScope = model.GetScopeFromSourceLocation(sourceLocation);
            if (currentScope == null)
            {
                Logger.Instance.Log("Leave Completion: can't get scope from source location.");
                return Result<CompletionResult, ResponseError>.Success(new CompletionResult(new CompletionItem[] { }));
            }

            var items = new List<CompletionItem>();

            switch (currentScope.GetType().Name)
            {
                case "Namespace":
                    items.AddRange(new[]
                    {
                        new CompletionItem { label = "class", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "enum", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "interface", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "fun", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "initial", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "event", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "namespace", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "var", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "val", kind = CompletionItemKind.Keyword },
                    });
                    break;
                case "Class":
                case "Enum":
                case "Interface":
                    items.AddRange(new[]
                    {
                        new CompletionItem { label = "fun", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "event", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "on", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "impl", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "var", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "val", kind = CompletionItemKind.Keyword },
                    });
                    break;
                case "Function":
                case "InitialRoutine":
                case "OnRoutine":
                    items.AddRange(new[]
                    {
                        new CompletionItem { label = "var", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "val", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "if", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "while", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "for", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "return", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "break", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "continue", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "yield", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "emit", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "var", kind = CompletionItemKind.Keyword },
                        new CompletionItem { label = "val", kind = CompletionItemKind.Keyword },
                    });
                    break;
            }

            Logger.Instance.Log($"Leave Completion: {items.Count}");
            return Result<CompletionResult, ResponseError>.Success(new CompletionResult(items.ToArray()));
        }

        protected override Result<CompletionItem, ResponseError> ResolveCompletionItem(CompletionItem @params)
        {
            return Result<CompletionItem, ResponseError>.Error(new ResponseError());
        }

        protected override VoidResult<ResponseError> Shutdown()
        {
            Logger.Instance.Log("Enter Shutdown()");
            // WORKAROUND: Language Server does not receive an exit notification.
            Task.Delay(1000).ContinueWith(_ => Environment.Exit(0));
            Logger.Instance.Log("Leave Shutdown");
            return VoidResult<ResponseError>.Success();
        }

        protected override Result<DocumentSymbolResult, ResponseError> DocumentSymbols(DocumentSymbolParams @params)
        {
            Logger.Instance.Log($"Enter DocumentSymbol(textDocument={@params.textDocument.uri})");

            var document = _documents.All.FirstOrDefault(d => d.uri == @params.textDocument.uri);
            if (document == null)
            {
                return Result<DocumentSymbolResult, ResponseError>.Error(new ResponseError
                {
                    code = ErrorCodes.InvalidParams,
                    message = "Document not found"
                });
            }

            var compilationResult = _documents.GetCompilationResult(document.uri);
            if (compilationResult?.Model == null)
            {
                return Result<DocumentSymbolResult, ResponseError>.Success(new DocumentSymbolResult(new DocumentSymbol[] { }));
            }

            var symbols = new List<DocumentSymbol>();
            foreach (var mergedNamespace in compilationResult.Model.Namespaces)
            {
                foreach (var ns in mergedNamespace.Namespaces)
                {
                    if (Path.GetFullPath(ns.SourceLocation.FileName) == ConvertUriToPath(document.uri))
                        symbols.AddRange(CollectDocumentSymbols(ns));
                }
            }

            Logger.Instance.Log("Leave DocumentSymbol");
            return Result<DocumentSymbolResult, ResponseError>.Success(symbols.ToArray());
        }

        protected override Result<LocationSingleOrArray, ResponseError> GotoDefinition(TextDocumentPositionParams @params)
        {
            Logger.Instance.Log($"Enter Definition(textDocument={@params.textDocument.uri}, position={@params.position.line}:{@params.position.character})");

            var document = _documents.All.FirstOrDefault(d => d.uri == @params.textDocument.uri);
            if (document == null)
            {
                Logger.Instance.Log("Leave Definition: document not found");
                return Result<LocationSingleOrArray, ResponseError>.Error(new ResponseError { code = ErrorCodes.InvalidParams, message = "Document not found" });
            }

            var model = _documents.GetCompilationResult(document.uri).LastSuccessModel;
            if (model == null)
            {
                Logger.Instance.Log("Leave Definition: no successful compilation result is available.");
                return Result<LocationSingleOrArray, ResponseError>.Error(new ResponseError { code = ErrorCodes.InternalError, message = "no successful compliation result" });
            }

            // Convert position to SourceLocation
            var sourceLocation = new SourceLocation(
                ConvertUriToPath(document.uri),
                "",
                (int)@params.position.line + 1,
                (int)@params.position.line + 1,
                (int)@params.position.character,
                (int)@params.position.character
            );

            // Find definition
            var definitionLocation = model.GetDefinitionFromSourceLocation(sourceLocation);
            if (definitionLocation == null)
            {
                Logger.Instance.Log("Leave Definition: no definition found");
                return Result<LocationSingleOrArray, ResponseError>.Success(new LocationSingleOrArray([]));
            }

            // Convert definition location to LSP Location
            var definition = definitionLocation.IsLeft ? definitionLocation.Left.SourceLocation : definitionLocation.Right.SourceLocation;
            var location = new Location
            {
                uri = ConvertPathToUri(definition.FileName),
                range = ConvertSourceLocation(definition)
            };

            Logger.Instance.Log($"Leave Definition: {definition}");
            return Result<LocationSingleOrArray, ResponseError>.Success(new LocationSingleOrArray(location));
        }

        private IEnumerable<DocumentSymbol> CollectDocumentSymbols(ISemanticScope scope)
        {
            SymbolKind kind;
            if (scope is Namespace)
                kind = SymbolKind.Namespace;
            else if (scope is BabyPenguin.SemanticNode.Enum)
                kind = SymbolKind.Enum;
            else if (scope is Class)
                kind = SymbolKind.Class;
            else if (scope is InitialRoutine)
                kind = SymbolKind.Method;
            else if (scope is OnRoutine)
                kind = SymbolKind.Method;
            else if (scope is Function)
                kind = SymbolKind.Function;
            else if (scope is Interface)
                kind = SymbolKind.Interface;
            else
                yield break;

            var current = new DocumentSymbol
            {
                name = scope.Name,
                kind = kind,
                range = ConvertSourceLocation(scope.SourceLocation),
                selectionRange = ConvertSourceLocation(scope.SourceLocation),
                children = scope.Children.SelectMany(CollectDocumentSymbols).ToArray(),
            };
            yield return current;
        }

    }

    public class TextDocumentManager
    {
        private readonly List<TextDocumentItem> _all = new List<TextDocumentItem>();
        private readonly ConcurrentDictionary<Uri, CompilationResult> _compilationResults = new();

        public IReadOnlyList<TextDocumentItem> All => _all;

        public void Add(TextDocumentItem document)
        {
            if (_all.Any(x => x.uri == document.uri))
            {
                return;
            }
            _all.Add(document);
            OnChanged(document);
        }

        public void Change(Uri uri, long version, TextDocumentContentChangeEvent[] changeEvents)
        {
            var index = _all.FindIndex(x => x.uri == uri);
            if (index < 0)
            {
                return;
            }
            var document = _all[index];
            if (document.version >= version)
            {
                return;
            }
            foreach (var ev in changeEvents)
            {
                Apply(document, ev);
            }
            document.version = version;
            OnChanged(document);
        }

        public CompilationResult GetCompilationResult(Uri uri)
        {
            return _compilationResults.GetValueOrDefault(uri);
        }

        public void UpdateCompilationResult(Uri uri, CompilationResult result)
        {
            if (!_compilationResults.ContainsKey(uri))
            {
                result.LastSuccessModel = result.Model;
                _compilationResults[uri] = result;
            }
            else
            {
                var existing = _compilationResults[uri];
                existing.Model = result.Model;
                if (result.Model != null)
                    existing.LastSuccessModel = result.Model;
            }
        }

        private void Apply(TextDocumentItem document, TextDocumentContentChangeEvent ev)
        {
            if (ev.range != null)
            {
                var startPos = GetPosition(document.text, (int)ev.range.start.line, (int)ev.range.start.character);
                var endPos = GetPosition(document.text, (int)ev.range.end.line, (int)ev.range.end.character);
                var newText = document.text.Substring(0, startPos) + ev.text + document.text.Substring(endPos);
                document.text = newText;
            }
            else
            {
                document.text = ev.text;
            }
        }

        private static int GetPosition(string text, int line, int character)
        {
            var pos = 0;
            for (; 0 <= line; line--)
            {
                var lf = text.IndexOf('\n', pos);
                if (lf < 0)
                {
                    return text.Length;
                }
                pos = lf + 1;
            }
            var linefeed = text.IndexOf('\n', pos);
            var max = 0;
            if (linefeed < 0)
            {
                max = text.Length;
            }
            else if (linefeed > 0 && text[linefeed - 1] == '\r')
            {
                max = linefeed - 1;
            }
            else
            {
                max = linefeed;
            }
            pos += character;
            return (pos < max) ? pos : max;
        }

        public void Remove(Uri uri)
        {
            var index = _all.FindIndex(x => x.uri == uri);
            if (index < 0)
            {
                return;
            }
            _all.RemoveAt(index);
        }

        public event EventHandler<TextDocumentChangedEventArgs> Changed;

        protected virtual void OnChanged(TextDocumentItem document)
        {
            Changed?.Invoke(this, new TextDocumentChangedEventArgs(document));
        }
    }

    public class TextDocumentChangedEventArgs : EventArgs
    {
        private readonly TextDocumentItem _document;

        public TextDocumentChangedEventArgs(TextDocumentItem document)
        {
            _document = document;
        }

        public TextDocumentItem Document => _document;
    }

    public class CompilationResult
    {
        public SemanticModel Model { get; set; }
        public SemanticModel LastSuccessModel { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(); // UTF8N for non-Windows platform
            var app = new App(Console.OpenStandardInput(), Console.OpenStandardOutput());
            Logger.Instance.Attach(app);
            try
            {
                app.Listen().Wait();
            }
            catch (AggregateException ex)
            {
                Console.Error.WriteLine(ex.InnerExceptions[0]);
                Environment.Exit(-1);
            }
        }
    }

    public class Logger
    {
        public static Logger Instance { get; } = new Logger();

        public Logger()
        {
        }

        private Proxy _proxy;

        public void Attach(Connection connection)
        {
            if (connection == null)
            {
                _proxy = null;
            }
            else
            {
                _proxy = new Proxy(connection);
            }
        }

        public void Error(string message) => Send(MessageType.Error, message);
        public void Warn(string message) => Send(MessageType.Warning, message);
        public void Info(string message) => Send(MessageType.Info, message);
        public void Log(string message) => Send(MessageType.Log, message);

        private void Send(MessageType type, string message)
        {
            this._proxy?.Window.LogMessage(new LogMessageParams
            {
                type = type,
                message = message
            });
        }
    }
}