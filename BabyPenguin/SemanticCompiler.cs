using System.Threading;

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

        /// <summary>
        /// CLI --define K=V pre-seed layer for the file-scoped conditional
        /// compilation store (#define/#if): visible to every file, and a code
        /// #define in a file overwrites it without error. Populate before
        /// <see cref="Compile"/>.
        /// </summary>
        public Dictionary<string, string> CliDefines { get; } = [];

        private static ulong counter = 0;

        public SemanticCompiler AddFile(string filePath)
        {
            Sources.Add(new SourceInput(filePath, null));
            return this;
        }

        public SemanticCompiler AddSource(string source, string? fileName = null)
        {
            Sources.Add(new SourceInput(fileName ?? $"anonymous_{Interlocked.Increment(ref counter)}", source));
            return this;
        }

        public SemanticModel Compile(bool addBuiltin = true)
        {
            var model = new SemanticModel(addBuiltin, Reporter) { CliDefines = CliDefines };

            foreach (var source in Sources)
            {
                model.AddSource(source.Source, source.File);
            }

            model.Compile();

            return model;
        }

        /// <summary>
        /// Adds all source files from a .penguins project file
        /// </summary>
        /// <param name="projectFilePath">Path to the .penguins file</param>
        /// <returns>This compiler instance for method chaining</returns>
        public SemanticCompiler AddProject(string projectFilePath)
        {
            var project = PenguinProject.Load(projectFilePath);
            var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? ".";
            var sourceFiles = project.ResolveSourceFiles(projectDirectory);

            foreach (var sourceFile in sourceFiles)
            {
                Sources.Add(new SourceInput(sourceFile, null));
            }

            return this;
        }
    }
}