namespace BabyPenguin
{
    public class SemanticCompiler
    {
        public SemanticCompiler(ErrorReporter? reporter = null)
        {
            Reporter = reporter ?? new ErrorReporter();
        }

        public ErrorReporter Reporter { get; }

        public List<PenguinParser> Parsers { get; } = [];

        private static ulong counter = 0;

        public SemanticCompiler AddFile(string filePath)
        {
            var parser = new PenguinParser(filePath, Reporter);
            Parsers.Add(parser);
            return this;
        }

        public SemanticCompiler AddSource(string source, string? fileName = null)
        {
            var parser = new PenguinParser(source, fileName ?? $"<anonymous_{counter++}>", Reporter);
            Parsers.Add(parser);
            return this;
        }

        public SemanticModel Compile()
        {
            var syntaxCompilers = Parsers.Select(parser =>
            {
                if (!parser.Parse() || parser.Result == null)
                {
                    Reporter.Throw("Failed to parse input: " + parser.SourceFile + "\n");
                    throw new NotImplementedException(); // never reached
                }
                else
                    return new SyntaxCompiler(parser.SourceFile, parser.Result, Reporter);
            }).ToList();

            foreach (var compiler in syntaxCompilers)
            {
                compiler.Compile();
            }

            var model = new SemanticModel(Reporter);
            foreach (var compiler in syntaxCompilers)
                foreach (var ns in compiler.Namespaces)
                    model.AddNamespace(new SemanticNode.Namespace(model, ns));

            model.Compile();

            return model;
        }
    }
}