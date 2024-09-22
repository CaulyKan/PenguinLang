using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using BabyPenguin.Semantic;
using PenguinLangAntlr;
using static PenguinLangAntlr.PenguinLangParser;
using System.Text;
using BabyPenguin;
using System.Reflection.Metadata.Ecma335;

namespace BabyPenguin
{
    public partial class SemanticModel
    {
        private Namespace AddBuiltins()
        {
            var ns = new Namespace(this, "__builtin");

            var println = new Function(this, ns, "println",
                new Dictionary<string, FunctionParameter>() { { "text", new FunctionParameter("text", TypeInfo.BuiltinTypes["string"], true) } },
                TypeInfo.BuiltinTypes["void"]);
            var print = new Function(this, ns, "print",
                new Dictionary<string, FunctionParameter>() { { "text", new FunctionParameter("text", TypeInfo.BuiltinTypes["string"], true) } },
                TypeInfo.BuiltinTypes["void"]);

            (ns as IRoutineContainer).AddFunction(println);
            (ns as IRoutineContainer).AddFunction(print);

            return ns;
        }
    }
}