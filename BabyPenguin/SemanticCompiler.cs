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

    public class SemanticModel
    {
        public SemanticModel(ErrorReporter reporter)
        {
            Reporter = reporter;
        }

        public void Compile(IEnumerable<SyntaxCompiler> compilers)
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
                Reporter.Write(DiagnosticLevel.Error, $"Type '{type.FullName}' already exists");
                throw new InvalidOperationException($"Type '{type.FullName}' already exists");
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

        public List<Semantic.Namespace> Namespaces { get; } = [];
        public List<Class> Classes { get; } = [];
        public List<Function> Functions { get; } = [];
        public List<TypeInfo> Types { get; } = [];
        public List<ISymbol> Symbols { get; } = [];
        public ErrorReporter Reporter { get; }
        public List<ICompilable> CompileTasks { get; } = [];
    }
}