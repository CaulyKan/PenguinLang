using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PenguinLangSyntax
{

    public interface IPrettyPrint
    {
        static string PrintText(int indentLevel, string text) => new string(' ', indentLevel * 2) + text;

        IEnumerable<string> PrettyPrint(int indentLevel, string? prefix = null)
        {
            yield return new string(' ', indentLevel * 2) + (prefix ?? " ") + ToString();
        }
    }


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
        public static PenguinLangParser.CompilationUnitContext Parse(string file, ErrorReporter? reporter = null)
        {
            return Parse(File.ReadAllText(file), file, reporter);
        }

        public static PenguinLangParser.CompilationUnitContext Parse(string source, string file, ErrorReporter? reporter_ = null)
        {
            var parser = PrepareParser(source, file, reporter_);
            var result = parser.Parser.compilationUnit();
            parser.ReportError();
            return result;
        }

        public static PenguinLangParser.InterfaceDefinitionContext ParseInterface(string source, string file, ErrorReporter? reporter_ = null)
        {
            var parser = PrepareParser(source, file, reporter_);
            var result = parser.Parser.interfaceDefinition();
            parser.ReportError();
            return result;
        }

        public static PenguinLangParser.ClassDefinitionContext ParseClass(string source, string file, ErrorReporter? reporter_ = null)
        {
            var parser = PrepareParser(source, file, reporter_);
            var result = parser.Parser.classDefinition();
            parser.ReportError();
            return result;
        }

        public static PenguinLangParser.NamespaceDefinitionContext ParseNamespace(string source, string file, ErrorReporter? reporter_ = null)
        {
            var parser = PrepareParser(source, file, reporter_);
            var result = parser.Parser.namespaceDefinition();
            parser.ReportError();
            return result;
        }

        public static PenguinLangParser.EnumDefinitionContext ParseEnum(string source, string file, ErrorReporter? reporter_ = null)
        {
            var parser = PrepareParser(source, file, reporter_);
            var result = parser.Parser.enumDefinition();
            parser.ReportError();
            return result;
        }

        private record ParserData(PenguinLangParser Parser, ErrorListener<int> LexerListener, ErrorListener<IToken> ParserListener)
        {
            public void ReportError()
            {
                if (ParserListener.HasError || LexerListener.HasError)
                    throw new PenguinLangException("Failed to parse input");
            }
        }

        private static ParserData PrepareParser(string source, string file, ErrorReporter? reporter_)
        {
            var reporter = reporter_ ?? new ErrorReporter();
            reporter.Write(ErrorReporter.DiagnosticLevel.Info, "parsing " + file);

            var str = new AntlrInputStream(source);
            var lexer = new PenguinLangLexer(str);
            var tokens = new CommonTokenStream(lexer);
            var parser = new PenguinLangParser(tokens);
            var listener_lexer = new ErrorListener<int>(reporter, file);
            var listener_parser = new ErrorListener<IToken>(reporter, file);
            lexer.RemoveErrorListeners();
            parser.RemoveErrorListeners();
            lexer.AddErrorListener(listener_lexer);
            parser.AddErrorListener(listener_parser);
            return new ParserData(parser, listener_lexer, listener_parser);
        }
    }
}