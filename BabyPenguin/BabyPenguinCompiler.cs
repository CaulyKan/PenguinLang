using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using PenguinLangAntlr;

namespace BabyPenguin
{
    public class BabyPenguinCompiler : ICompilerInfo
    {
        public BabyPenguinCompiler(string file, PenguinLangParser.CompilationUnitContext ast, ErrorReporter reporter)
        {
            Ast = ast;
            Reporter = reporter;
            FileName = file;
        }

        public void Compile()
        {
            new Namespace(this, Ast);
        }

        public string PrintSemanticTree()
        {
            return string.Join("\n", Namespaces.Values.SelectMany(x => x.PrettyPrint(0)));
        }

        public PenguinLangParser.CompilationUnitContext Ast { get; }
        public ErrorReporter Reporter { get; }
        public string FileName { get; set; }
        public Stack<Namespace> NamespaceStack { get; } = new Stack<Namespace>();
        public Dictionary<string, Namespace> Namespaces { get; } = new Dictionary<string, Namespace>();
        public IRoutine? CurrentRoutine { get; set; } = null;
        public List<IRoutine> Routines { get; } = new();
    }

}