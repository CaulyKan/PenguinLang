using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;
using System.Text;

namespace BabyPenguin
{
    using BabyPenguin.Syntax;
    using Semantic;

    public partial class SemanticModel
    {
        public SemanticModel(ErrorReporter reporter)
        {
            BuiltinNamespace = AddBuiltins();

            Reporter = reporter;
        }

        public void CompileSyntax(IEnumerable<SyntaxCompiler> compilers)
        {
            foreach (var compiler in compilers)
            {
                foreach (var ns in compiler.Namespaces)
                {
                    Namespaces.Add(new Semantic.Namespace(this, ns));
                }
            }

            Reporter.Write(DiagnosticLevel.Debug, $"Semantic Scopes:\n" + string.Join("\n", Namespaces.OfType<IPrettyPrint>().SelectMany(s => s.PrettyPrint(0))));

            Reporter.Write(DiagnosticLevel.Debug, $"Type Table:\n" + string.Join("\n", Types.Select(t => "\t" + t.FullName)));

            Namespaces.ForEach(ns => ns.ElabSyntaxSymbols());

            Reporter.Write(DiagnosticLevel.Debug, $"Symbol Table:\n" + string.Join("\n", Symbols.Select(t => "\t" + t.FullName + ": " + t.Type.FullName)));

            foreach (var task in CompileTasks)
            {
                task.CompileSyntaxStatements();
            }
        }

        public TypeInfo? ResolveType(string name)
        {
            if (TypeInfo.BuiltinTypes.TryGetValue(name, out TypeInfo? value))
            {
                return value;
            }

            return Types.FirstOrDefault(t => t.FullName == name);
        }

        public TypeInfo CreateType(string name, string namespace_, List<TypeInfo> genericArguments)
        {
            var type = new TypeInfo(name, namespace_, genericArguments);
            if (Types.Any(t => t.FullName == type.FullName))
            {
                Reporter.Throw($"Type '{type.FullName}' already exists", SourceLocation.Empty());
            }
            Types.Add(type);
            return type;
        }

        public TypeInfo ResolveOrCreateType(string name, string namespace_, List<TypeInfo> genericArguments)
        {
            var type = new TypeInfo(name, namespace_, genericArguments);
            var existingType = Types.FirstOrDefault(t => t.FullName == type.FullName);
            if (existingType != null)
            {
                return existingType;
            }
            else
            {
                Types.Add(type);
                return type;
            }
        }

        public Semantic.Namespace BuiltinNamespace { get; }
        public List<Semantic.Namespace> Namespaces { get; } = [];
        public List<Class> Classes { get; } = [];
        public List<Function> Functions { get; } = [];
        public List<TypeInfo> Types { get; } = [];
        public List<ISymbol> Symbols { get; } = [];
        public ErrorReporter Reporter { get; }
        public List<ICodeContainer> CompileTasks { get; } = [];
    }

    public class SemanticCompiler
    {
        public SemanticCompiler()
        {

        }

        ErrorReporter Reporter { get; } = new ErrorReporter();

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
            model.CompileSyntax(syntaxCompilers);
            return model;
        }
    }
}