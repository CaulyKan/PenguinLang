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
            var model = new SemanticModel(Reporter);

            foreach (var source in Sources)
            {
                model.AddSource(source.Source, source.File);
            }

            model.Compile();

            return model;
        }
    }
}