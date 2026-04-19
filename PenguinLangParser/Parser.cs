using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PenguinLangParser
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

        public ParserRuleContext? CurrentContext { get; set; }

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
            CurrentContext = (recognizer as PenguinLangParser)?.Context as ParserRuleContext;
            Reporter.Write(DiagnosticLevel.Error, msg, loc);
            // base.SyntaxError(output, recognizer, offendingSymbol, line, col, msg, e);
        }
    }

    public class PenguinParser
    {
        public static void GetContext(string s)
        {

        }

        public static PenguinLangParser.CompilationUnitContext Parse(string source, string file, ErrorReporter? reporter_ = null)
        {
            var parser = PrepareParser(source, file, reporter_);
            var result = parser.Parser.compilationUnit();
            parser.ReportError();
            return result;
        }

        public static T Parse<T>(string source, string file, Func<PenguinLangParser, T> func, ErrorReporter? reporter_ = null)
        {
            var parser = PrepareParser(source, file, reporter_);
            var result = func(parser.Parser);
            try
            {
                parser.ReportError();
            }
            catch (PenguinLangException e)
            {
                throw new PenguinLangException(e.Message + "\n\nSource:\n" + source, e.CurrentContext);
            }
            return result;
        }

        public record ParserData(PenguinLangParser Parser, ErrorListener<int> LexerListener, ErrorListener<IToken> ParserListener)
        {
            public void ReportError()
            {
                if (ParserListener.HasError || LexerListener.HasError)
                {
                    var rule = (ParserListener.CurrentContext ?? LexerListener.CurrentContext)?.RuleIndex;

                    var err = string.Join("\n", ParserListener.Reporter.Messages.Where(i => i.Level == DiagnosticLevel.Error).Select(i => i.Message + " @ " + i.SourceLocation?.ToString()));

                    throw new PenguinLangException("Failed to parse input, messages: \n" + err, rule == null ? null : Parser.RuleNames[rule.Value]);
                }
            }
        }

        public static ParserData PrepareParser(string source, string file, ErrorReporter? reporter_)
        {
            var reporter = reporter_ ?? new ErrorReporter();
            reporter.Write(DiagnosticLevel.Info, "parsing " + file);

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

        /// <summary>
        /// Parses source code and returns S-expression string representation of the AST.
        /// </summary>
        public static string ParseToSexp(string source, string file, ErrorReporter? reporter_ = null)
        {
            var reporter = reporter_ ?? new ErrorReporter();
            var compilationUnit = Parse(source, file, reporter);
            var walker = new SyntaxWalker(file, reporter);
            var namespaceDef = SyntaxNode.Build<NamespaceDefinition>(walker, compilationUnit);
            return SexpSerializer.Serialize(namespaceDef);
        }
    }
}