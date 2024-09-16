// See https://aka.ms/new-console-template for more information
using System.Text;
using CommandLine;
using PenguinLangAntlr;
namespace BabyPenguin
{
    public class Options
    {

        [Value(0, Required = true, HelpText = "Input files to process")]
        public required IEnumerable<string> Files { get; set; }
    }

    public class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<Options>(args).MapResult(
                Run,
                _ => -1
            );
        }

        static int Run(Options options)
        {
            var reporter = new ErrorReporter();
            try
            {
                var parsers = options.Files.Select(file => new PenguinParser(file, reporter));

                var compilers = parsers.Select(parser =>
                {
                    if (!parser.Parse() || parser.Result == null)
                        throw new Exception("Failed to parse input: " + parser.SourceFile);
                    else
                        return new SyntaxCompiler(parser.SourceFile, parser.Result, reporter);
                }).ToList();

                foreach (var compiler in compilers)
                {
                    compiler.Compile();
                    Console.WriteLine($"Syntax Tree for {compiler.FileName}:");
                    Console.WriteLine(compiler.PrintSyntaxTree());
                }

                var semanticCompiler = new SemanticModel(reporter);
                semanticCompiler.Compile(compilers);
                // Console.WriteLine("\nSymbol Table:");
                // Console.WriteLine(semanticCompiler.PrintSymbolTable());

                reporter.Write(DiagnosticLevel.Info, "All done.");
                return 0;
            }
            catch (Exception e)
            {
                reporter.Write(DiagnosticLevel.Error, e.Message);
                return -1;
            }
        }
    }
}