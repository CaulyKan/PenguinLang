namespace PenguinLangAntlr
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public class ErrorListener<S> : ConsoleErrorListener<S>
    {
        public bool HasError { get; private set; } = false;

        public ErrorReporter Reporter { get; }

        public string File { get; }

        public ErrorListener(ErrorReporter reporter, string file)
        {
            File = file;
            Reporter = reporter;
        }

        public override void SyntaxError(TextWriter output, IRecognizer recognizer, S offendingSymbol, int line,
            int col, string msg, RecognitionException e)
        {
            HasError = true;
            var loc = new SourceLocation(File, "", line, line, col, col);
            Reporter.Write(ErrorReporter.DiagnosticLevel.Error, msg, loc);
            // base.SyntaxError(output, recognizer, offendingSymbol, line, col, msg, e);
        }
    }

    public class PenguinParser
    {
        public PenguinParser(string file, ErrorReporter? reporter = null)
        {
            this.SourceFile = file;
            this.Source = File.ReadAllText(file);
            this.Reporter = reporter ?? new ErrorReporter();
        }

        public PenguinParser(string source, string file, ErrorReporter? reporter = null)
        {
            this.SourceFile = file;
            this.Source = source;
            this.Reporter = reporter ?? new ErrorReporter();
        }

        public ErrorReporter Reporter { get; }
        public string SourceFile { get; set; }
        public string Source { get; set; }

        public PenguinLangParser.CompilationUnitContext? Result { get; private set; }

        public bool Parse()
        {
            bool success = true;
            Reporter.Write(ErrorReporter.DiagnosticLevel.Info, "parsing " + SourceFile);

            var str = new AntlrInputStream(Source);
            var lexer = new PenguinLangLexer(str);
            var tokens = new CommonTokenStream(lexer);
            var parser = new PenguinLangParser(tokens);
            var listener_lexer = new ErrorListener<int>(Reporter, SourceFile);
            var listener_parser = new ErrorListener<IToken>(Reporter, SourceFile);
            lexer.RemoveErrorListeners();
            parser.RemoveErrorListeners();
            lexer.AddErrorListener(listener_lexer);
            parser.AddErrorListener(listener_parser);
            Result = parser.compilationUnit();
            if (listener_lexer.HasError || listener_parser.HasError)
            {
                success = false;
            }
            return success;
        }
    }
}