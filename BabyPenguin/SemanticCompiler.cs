namespace BabyPenguin
{
    public class SemanticCompiler
    {
        public SemanticCompiler(ErrorReporter? reporter = null)
        {
            Reporter = reporter ?? new ErrorReporter();
        }

        public ErrorReporter Reporter { get; }

        public record SourceInput(string File, string? Source);

        public List<SourceInput> Sources { get; } = [];

        private static ulong counter = 0;

        public SemanticCompiler AddFile(string filePath)
        {
            Sources.Add(new SourceInput(filePath, null));
            return this;
        }

        public SemanticCompiler AddSource(string source, string? fileName = null)
        {
            Sources.Add(new SourceInput(fileName ?? $"<anonymous_{counter++}>", source));
            return this;
        }

        public SemanticModel Compile()
        {
            var syntaxCompilers = Sources.Select(s =>
            {
                var root = s.Source == null ? PenguinParser.Parse(s.File, Reporter) : PenguinParser.Parse(s.Source, s.File, Reporter);
                return new SyntaxCompiler(s.File, root, Reporter);
            }).ToList();

            foreach (var compiler in syntaxCompilers)
            {
                compiler.Compile();
            }

            var model = new SemanticModel(Reporter);
            foreach (var compiler in syntaxCompilers)
                foreach (var ns in compiler.Namespaces)
                    model.AddNamespace(new Namespace(model, ns));

            model.Compile();

            return model;
        }
    }
}