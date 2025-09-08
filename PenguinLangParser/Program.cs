using System.IO;

namespace PenguinLangParser;

public class Program
{
    public static void Main(string[] args)
    {
        string? inputFile = null;
        string? outputFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--input" && i + 1 < args.Length)
            {
                inputFile = args[++i];
            }
            else if (args[i] == "--output" && i + 1 < args.Length)
            {
                outputFile = args[++i];
            }
        }

        if (inputFile == null || outputFile == null)
        {
            Console.Error.WriteLine("Usage: PenguinParser --input <file> --output <file>");
            return;
        }

        try
        {
            var source = File.ReadAllText(inputFile);
            var reporter = new ErrorReporter(new StringWriter());
            var ast = PenguinParser.Parse(source, inputFile, reporter);
            var compiler = new SyntaxCompiler(inputFile, ast, reporter);
            compiler.Compile();
            var sexp = SExpSerializer.Serialize(compiler.Namespaces);
            if (outputFile == "stdout")
                Console.WriteLine(sexp);
            else
                File.WriteAllText(outputFile, sexp);
            Console.WriteLine($"Successfully parsed {inputFile} and wrote S-expression to {outputFile}.");
        }
        catch (PenguinLangException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (ex.CurrentContext != null)
            {
                Console.Error.WriteLine($"At: {ex.CurrentContext}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"An unexpected error occurred: {ex.Message}");
        }
    }
}
