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
    using ConsoleTables;
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
            var compilersList = compilers.ToList();
            SyntaxCopmilers.AddRange(compilersList);

            foreach (var compiler in compilersList)
            {
                foreach (var ns in compiler.Namespaces)
                {
                    Namespaces.Add(new Semantic.Namespace(this, ns));
                }
            }

            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Semantic Scopes:\n" + string.Join("\n", Namespaces.OfType<IPrettyPrint>().SelectMany(s => s.PrettyPrint(0))));

            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Type Table:\n" + PrintTypeTable());

            Namespaces.ForEach(ns => ns.ElabSyntaxSymbols());

            Reporter.Write(ErrorReporter.DiagnosticLevel.Debug, $"Symbol Table:\n" + PrintSymbolTable());

            foreach (var task in CompileTasks)
            {
                task.CompileSyntaxStatements();
            }
        }

        public string PrintTypeTable()
        {
            var table = new ConsoleTable("Name");
            Types.ForEach(s => table.AddRow(s.FullName));
            return table.ToMarkDownString();
        }

        public string PrintSymbolTable()
        {
            var table = new ConsoleTable("Name", "Type", "Source");
            Symbols.ForEach(s => table.AddRow(s.FullName, s.TypeInfo.ToString(), s.SourceLocation.ToString()));
            return table.ToMarkDownString();
        }

        public List<ISymbol> ResolveClassSymbols(TypeInfo classType)
        {
            if (classType.IsClassType)
            {
                return Symbols.Where(s => s.Parent.FullName == classType.FullName).ToList();
            }
            else
            {
                throw new PenguinLangException("Parameter for ResolveClassSymbols must be a class type");
            }
        }

        public ISymbol? ResolveSymbol(string name)
        {
            return Symbols.FirstOrDefault(s => s.FullName == name);
        }

        static string BuildFullName(string name, ISemanticScope? scope = null)
        {
            if (scope != null)
            {
                while (scope as Semantic.Namespace == null)
                {
                    scope = scope!.Parent;
                }

                var ns = scope as Semantic.Namespace;
                return ns!.FullName + "." + name;
            }
            else return name;
        }

        public TypeInfo? ResolveType(string name, ISemanticScope? scope = null)
        {
            if (TypeInfo.BuiltinTypes.TryGetValue(name, out TypeInfo? value))
                return value;

            return Types.FirstOrDefault(t => t.FullName == BuildFullName(name, scope));
        }

        public Class? ResolveClass(string name, ISemanticScope? scope = null)
        {
            var fullname = BuildFullName(name, scope);
            return Classes.FirstOrDefault(c => (c as ISemanticScope).FullName == fullname);
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
        public List<SyntaxCompiler> SyntaxCopmilers { get; } = [];
    }

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
            model.CompileSyntax(syntaxCompilers);
            return model;
        }
    }
}